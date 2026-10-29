using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Services;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// One blocking A2A 0.3 <c>message/send</c> to <c>{kagent}/api/a2a/{ns}/{agent}/</c>, the same call a ticket stage
/// makes; every step is a fresh kagent session. The prompt carries what a person typed and earlier answers, which may
/// hold a secret, so this call follows D322 as a secret-carrying one: a refusal logs method, path, status and
/// <see cref="KAgentApiClient.SecretCallRefused"/>, never kagent's body, and the exception says only a category.
/// </summary>
internal sealed partial class KAgentInvestigationClient(
    HttpClient http, IOptions<InvestigateOptions> options, ILogger<KAgentInvestigationClient> logger) : IInvestigationAgentClient
{
    public async Task<string> AskAsync(string agentRef, string prompt, CancellationToken ct)
    {
        var (ns, name) = AgentRef.Split(agentRef);
        var path = $"api/a2a/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/";
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            // Buffered, so it carries a Content-Length: some proxies in front of kagent refuse a chunked body.
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
        request.Headers.Add("A2A-Version", "0.3");
        request.Headers.Add("X-User-Id", options.Value.KAgentUserId);

        JsonNode? body;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(logger, "POST", path, (int)response.StatusCode, KAgentApiClient.SecretCallRefused);
                throw new AgentCallException($"kagent answered HTTP {(int)response.StatusCode}");
            }
            body = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        }
        catch (HttpRequestException) when (ct.IsCancellationRequested)
        {
            // Cancelled mid-send (the run's cancel or the step's deadline): some handlers surface that as an I/O error.
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new AgentCallException("kagent answered invalid JSON");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Type name only: the exception text can carry the controller URL.
            throw new AgentCallException($"kagent is unreachable ({ex.GetType().Name})");
        }

        try
        {
            return Answer(body!, path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or FormatException or ArgumentException)
        {
            throw new AgentCallException("kagent answered in an unexpected shape");
        }
    }

    private string Answer(JsonNode body, string path)
    {
        if (body["error"] is { } error)
        {
            LogRefused(logger, "POST", path, 200, KAgentApiClient.SecretCallRefused);
            throw new AgentCallException($"kagent refused the call (JSON-RPC {error["code"]?.GetValue<int>()})");
        }

        var result = body["result"] ?? throw new AgentCallException("kagent answered without a result");
        if (result["kind"]?.GetValue<string>() == "message")
            return NonEmpty(Text(result["parts"]));

        var state = result["status"]!["state"]!.GetValue<string>();
        if (state != "completed")
            throw new AgentCallException(state.Length <= 32 ? $"the agent's task ended '{state}'" : "kagent answered in an unexpected shape");

        var artifacts = result["artifacts"]?.AsArray();
        var text = artifacts is { Count: > 0 } ? Text(artifacts[^1]!["parts"]) : "";
        return NonEmpty(text.Length > 0 ? text : Text(result["status"]!["message"]?["parts"]));
    }

    /// <summary>The answer's text parts joined, reasoning ("thought") parts skipped and NUL removed (Postgres text cannot hold it).</summary>
    private static string Text(JsonNode? parts) =>
        parts is null
            ? ""
            : string.Concat(parts.AsArray()
                .Where(p => p!["kind"]?.GetValue<string>() == "text" && !IsThought(p))
                .Select(p => p!["text"]!.GetValue<string>())).Replace("\0", "");

    private static bool IsThought(JsonNode part) =>
        part["metadata"] is JsonObject metadata &&
        (metadata["kagent_thought"]?.GetValue<bool>() == true || metadata["adk_thought"]?.GetValue<bool>() == true);

    private static string NonEmpty(string text) =>
        text.Trim().Length > 0 ? text : throw new AgentCallException("the agent answered with no text");

    [LoggerMessage(Level = LogLevel.Warning, Message = "kagent {Method} {Path} answered {StatusCode}: {Detail}")]
    private static partial void LogRefused(ILogger logger, string method, string path, int statusCode, string detail);
}
