using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// One blocking A2A 0.3 <c>message/send</c> to <c>{kagent}/api/a2a/{ns}/{agent}/</c> (kagent 0.10.x). Each call
/// omits <c>contextId</c>, so every stage attempt is a fresh kagent session. The answer is the text of the
/// completed task's last artifact. Anything else — an error, a failed task, or a body of an unexpected shape —
/// is a <see cref="StageAgentException"/> with a fixed category; kagent's own error text goes to the log only.
/// </summary>
public sealed partial class KAgentStageClient(HttpClient http, IOptions<TicketsOptions> options, ILogger<KAgentStageClient> logger)
    : IStageAgentClient
{
    public async Task<string> AskAsync(StageAgent agent, string prompt, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"api/a2a/{Uri.EscapeDataString(agent.Namespace)}/{Uri.EscapeDataString(agent.Name)}/")
        {
            // Buffered, so it carries a Content-Length: a streamed (chunked) body is refused by some proxies in front of kagent.
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "message/send",
                @params = new
                {
                    message = new
                    {
                        kind = "message",
                        messageId = Guid.NewGuid().ToString(),
                        role = "user",
                        parts = new[] { new { kind = "text", text = prompt } }
                    },
                    configuration = new { blocking = true }
                }
            }), Encoding.UTF8, "application/json")
        };
        // Pins the wire version: kagent plans to change the no-header default in 0.11.
        request.Headers.Add("A2A-Version", "0.3");
        request.Headers.Add("X-User-Id", options.Value.KAgentUserId);

        JsonNode? body;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new StageAgentException($"kagent answered HTTP {(int)response.StatusCode}");
            body = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: an unknown charset in the reply's content type.
            throw new StageAgentException("kagent answered invalid JSON");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Type name only: the exception text can carry the controller URL.
            throw new StageAgentException($"kagent is unreachable ({ex.GetType().Name})");
        }

        try
        {
            return Answer(body!);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or FormatException or ArgumentException)
        {
            throw new StageAgentException("kagent answered in an unexpected shape");
        }
    }

    private string Answer(JsonNode body)
    {
        if (body["error"] is { } error)
        {
            LogKAgentError(logger, TextCut.Take(error.ToJsonString(), 1_000));
            throw new StageAgentException($"kagent refused the call (JSON-RPC {error["code"]!.GetValue<int>()})");
        }

        var result = body["result"] ?? throw new StageAgentException("kagent answered without a result");
        if (result["kind"]?.GetValue<string>() == "message")
            return NonEmpty(Text(result["parts"]));

        var state = result["status"]!["state"]!.GetValue<string>();
        if (state.Length > 32)
            throw new StageAgentException("kagent answered in an unexpected shape");
        if (state != "completed")
        {
            LogTaskEnded(logger, state, TextCut.Take(Text(result["status"]!["message"]?["parts"]), 1_000));
            throw new StageAgentException($"the agent's task ended '{state}'");
        }

        var artifacts = result["artifacts"]?.AsArray();
        var text = artifacts is { Count: > 0 } ? Text(artifacts[^1]!["parts"]) : "";
        return NonEmpty(text.Length > 0 ? text : Text(result["status"]!["message"]?["parts"]));
    }

    /// <summary>
    /// The answer's text parts joined; NUL removed, because Postgres text cannot store it. Reasoning ("thought")
    /// parts are skipped: kagent sends them as ordinary text parts marked in metadata, and the verdict is the
    /// first line of the answer, not of the model's thinking.
    /// </summary>
    private static string Text(JsonNode? parts) =>
        parts is null
            ? ""
            : string.Concat(parts.AsArray()
                .Where(p => p!["kind"]?.GetValue<string>() == "text" && !IsThought(p))
                .Select(p => p!["text"]!.GetValue<string>())).Replace("\0", "");

    // kagent's Python runtime marks thoughts `kagent_thought`, its Go runtime (adk-go) `adk_thought`.
    private static bool IsThought(JsonNode part) =>
        part["metadata"] is JsonObject metadata &&
        (metadata["kagent_thought"]?.GetValue<bool>() == true || metadata["adk_thought"]?.GetValue<bool>() == true);

    private static string NonEmpty(string text) =>
        text.Trim().Length > 0 ? text : throw new StageAgentException("the agent answered with no text");

    [LoggerMessage(Level = LogLevel.Warning, Message = "kagent JSON-RPC error: {Error}")]
    private static partial void LogKAgentError(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "kagent task ended {State}: {Detail}")]
    private static partial void LogTaskEnded(ILogger logger, string state, string detail);
}
