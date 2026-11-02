using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;
using Skanyxx.Module.Chat.Features;
using Skanyxx.Module.Chat.Features.Turns;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D553: a turn kagent ends badly keeps kagent's own reason — cleaned (credentials blanked, no control characters, cut
/// visibly) — for the log and for builders and up, beside the fixed words everyone sees. Streamed (<see cref="A2ATurn"/>)
/// and blocking (<see cref="KAgentApiClient.SendMessageAsync"/>) alike.
/// </summary>
public sealed class ChatFailureReasonTests
{
    /// <summary>What kagent answered for kagent/seed when its memory tool server was down (2026-11-02).</summary>
    public const string Observed =
        "failed to extract tools from the tool set \"mcp_tool_set\": failed to list MCP tools: calling \"tools/list\": sending \"tools/list\": "
        + "rejected by transport: Post \"http://host.docker.internal:5287/mcp/memory\": dial tcp 0.250.250.254:5287: connect: connection refused";

    private const string Secret = "sk-live-abcdefghijklmnop1234567890";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheObservedReason_ComesThroughWhole()
    {
        Assert.Equal(Observed, ChatFailureReason.Of(Observed));
    }

    [Fact]
    public void AReason_HasItsCredentialsBlanked_AndNoLineOrControlCharacters()
    {
        var reason = ChatFailureReason.Of($"401 from tool: Authorization: Bearer {Secret}\n[ERR] forged line\r\u2028\u202Egnp.exe\u001b[31m api_key={Secret}")!;

        Assert.DoesNotContain(Secret, reason);
        Assert.Contains(SecretScan.Redacted, reason);
        Assert.Contains("forged line", reason);
        Assert.DoesNotContain(reason, c => char.IsControl(c) || c is '\u2028' or '\u202E');
    }

    /// <summary>Shapes kagent's Go errors quote from request URLs and headers that the shared redactors leave (security review 1).</summary>
    [Theory]
    [InlineData("GET https://maps.example/api?key=AbC123xyz&q=1 failed", "AbC123xyz")]
    [InlineData("GET https://h.example/x?auth=s3cr3tv4lue: 403", "s3cr3tv4lue")]
    [InlineData("PUT https://b.s3.example/o?X-Amz-Signature=deadbeefcafe0011 denied", "deadbeefcafe0011")]
    [InlineData("Ocp-Apim-Subscription-Key: 0123456789abcdef0123456789abcdef rejected", "0123456789abcdef0123456789abcdef")]
    [InlineData("x-functions-key=Zz9yYy8xXx7 unauthorized", "Zz9yYy8xXx7")]
    [InlineData("Post \"https://ghp1234shorttok@git.example/repo\": 401", "ghp1234shorttok")]
    [InlineData("Authorization: Bearer abc123 was refused", "abc123")]
    [InlineData("\"api_token\": \"t0k3n-v4l\" invalid", "t0k3n-v4l")]
    public void CredentialShapesFromUrlsAndHeaders_AreBlanked(string text, string secret)
    {
        var reason = ChatFailureReason.Of(text)!;

        Assert.DoesNotContain(secret, reason);
        Assert.Contains(SecretScan.Redacted, reason);
    }

    [Fact]
    public void ALongReason_IsCut_AndSaysSo()
    {
        var reason = ChatFailureReason.Of(new string('x', 3_000) + " the end")!;

        Assert.StartsWith(new string('x', ChatFailureReason.MaxLength) + "…", reason);
        Assert.EndsWith("(cut: kagent's message has 3,008 characters)", reason);
        Assert.DoesNotContain("the end", reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Silence_IsNoReason(string? text) => Assert.Null(ChatFailureReason.Of(text));

    [Fact]
    public void AStreamThatFails_KeepsKagentsWordsApart_FromWhatItShows()
    {
        var turn = new A2ATurn();

        var events = turn.Read(Node("""{"kind":"task","id":"t-1","status":{"state":"submitted"}}""")).ToList();
        events.AddRange(turn.Read(Node("""
            {"kind":"status-update","taskId":"t-1","final":true,"status":{"state":"failed","message":{"role":"agent","parts":[
              {"kind":"text","text":"thinking…","metadata":{"adk_thought":true}},
              {"kind":"text","text":
            """ + System.Text.Json.JsonSerializer.Serialize(Observed) + "}]}}}")));

        Assert.Equal(ChatFailure.Unfinished, turn.Failure);
        Assert.Equal("failed", turn.FailedState);
        Assert.Equal(Observed, turn.FailureDetail);
        Assert.Equal("t-1", turn.TaskId);
        Assert.DoesNotContain(events, e => e.Type is "answer" or "text" or "delta");
    }

    [Fact]
    public void AnApprovalStop_IsNoFailureReason()
    {
        var turn = new A2ATurn();

        _ = turn.Read(Node("""{"kind":"status-update","taskId":"t-3","final":true,"status":{"state":"auth-required","message":{"role":"agent","parts":[{"kind":"text","text":"Sign in at https://idp.example/?state=abc"}]}}}""")).ToList();

        Assert.Equal(ChatFailure.NeedsApproval, turn.Failure);
        Assert.Null(turn.FailedState);
        Assert.Null(turn.FailureDetail);
    }

    [Fact]
    public void AStreamThatCompletes_HasNoFailure()
    {
        var turn = new A2ATurn();

        _ = turn.Read(Node("""{"kind":"status-update","taskId":"t-2","final":true,"status":{"state":"completed","message":{"role":"agent","parts":[{"kind":"text","text":"Hi"}]}}}""")).ToList();

        Assert.Null(turn.Failure);
        Assert.Null(turn.FailedState);
        Assert.Null(turn.FailureDetail);
    }

    [Fact]
    public async Task ABlockingTurnThatFails_ThrowsTheReason_AndLogsItWithTheTask()
    {
        var body = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["kind"] = "task", ["id"] = "task-42",
                ["status"] = new JsonObject
                {
                    ["state"] = "failed",
                    ["message"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = $"{Observed} token={Secret}" }) }
                }
            }
        };
        var logger = new ListLogger<KAgentApiClient>();
        var client = new KAgentApiClient(new HttpClient(new Answering(body.ToJsonString())),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["KAgent:IngressUrl"] = "http://kagent.test" }).Build(), logger);

        var failed = await Assert.ThrowsAsync<KAgentTaskFailedException>(() => client.SendMessageAsync("kagent", "seed", "s1", "hi", "ana", Ct));

        Assert.Equal("the agent did not complete the answer", failed.Message);
        Assert.Equal(("task-42", "failed"), (failed.TaskId, failed.State));
        Assert.StartsWith(Observed, failed.Reason);
        Assert.DoesNotContain(Secret, failed.Reason);
        var line = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains("task-42", line.Message);
        Assert.Contains("kagent/seed", line.Message);
        Assert.Contains("connection refused", line.Message);
        Assert.DoesNotContain(Secret, line.Message);
    }

    private static JsonNode Node(string json) => JsonNode.Parse(json)!;

    private sealed class Answering(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
