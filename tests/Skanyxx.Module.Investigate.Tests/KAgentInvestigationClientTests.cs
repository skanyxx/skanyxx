using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;
using Skanyxx.Module.Investigate.Agents;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>The A2A call (D342) and its D322 rule: a refusal is logged by method, path and status only, never kagent's body.</summary>
public sealed class KAgentInvestigationClientTests
{
    private const string Secret = "sk-live-INVESTIGATE-0123456789abcdef";

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Recorder : ILogger<KAgentInvestigationClient>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(formatter(state, exception) + exception);
    }

    /// <summary>Cancels the call's token, then fails the way a torn connection does.</summary>
    private sealed class CancelThenFail(CancellationTokenSource cancel) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await cancel.CancelAsync();
            throw new HttpRequestException("The response ended prematurely.");
        }
    }

    [Fact]
    public async Task AnIoErrorAfterTheCallWasCancelled_IsACancellation_NotAnAgentFailure()
    {
        using var cancel = new CancellationTokenSource();
        var http = new HttpClient(new CancelThenFail(cancel)) { BaseAddress = new Uri("http://kagent.test/") };
        var client = new KAgentInvestigationClient(http, Options.Create(new InvestigateOptions()), new Recorder());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AskAsync("team-a/writer", "Why?", cancel.Token));
    }

    private static (KAgentInvestigationClient Client, Handler Handler, Recorder Log) Create(HttpStatusCode status, string body)
    {
        var handler = new Handler(status, body);
        var log = new Recorder();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://kagent.test/") };
        return (new KAgentInvestigationClient(http, Options.Create(new InvestigateOptions()), log), handler, log);
    }

    [Fact]
    public async Task ACompletedTask_AnswersWithTheLastArtifactsText_WithoutThoughts()
    {
        var (client, handler, _) = Create(HttpStatusCode.OK, """
            {"jsonrpc":"2.0","id":"1","result":{"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[
              {"kind":"text","text":"thinking","metadata":{"kagent_thought":true}},{"kind":"text","text":"The cause is X."}]}]}}
            """);

        var answer = await client.AskAsync("team-a/writer", "Why?", TestContext.Current.CancellationToken);

        Assert.Equal("The cause is X.", answer.Text);
        Assert.Equal(ChatUsage.None, answer.Usage);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://kagent.test/api/a2a/team-a/writer/", request.RequestUri!.ToString());
        Assert.Equal("0.3", request.Headers.GetValues("A2A-Version").Single());
        Assert.Equal("skanyxx-investigate", request.Headers.GetValues("X-User-Id").Single());
        Assert.Contains("\"method\":\"message/send\"", body);
        Assert.Contains("\"blocking\":true", body);
    }

    [Fact]
    public async Task ReportsTheTokensKAgentCounted()
    {
        var (client, _, _) = Create(HttpStatusCode.OK, """
            {"result":{"kind":"task","status":{"state":"completed"},"metadata":{"kagent_usage_metadata":{"promptTokenCount":100,"candidatesTokenCount":20,"totalTokenCount":150}},
             "artifacts":[{"parts":[{"kind":"text","text":"done"}]}]}}
            """);

        var answer = await client.AskAsync("team-a/writer", "Why?", TestContext.Current.CancellationToken);

        Assert.Equal(new ChatUsage(100, 50, 150), answer.Usage);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":\"echo: " + Secret + "\"}", "kagent answered HTTP 500")]
    [InlineData(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"echo: " + Secret + "\"}}", "kagent refused the call (JSON-RPC -32603)")]
    public async Task ARefusal_LogsNoBodyAndNoPrompt_AndSaysOnlyACategory(HttpStatusCode status, string body, string message)
    {
        var (client, _, log) = Create(status, body);

        var error = await Assert.ThrowsAsync<AgentCallException>(() => client.AskAsync("kagent/researcher", $"look at {Secret}", TestContext.Current.CancellationToken));

        Assert.Equal(message, error.Message);
        var line = Assert.Single(log.Lines);
        Assert.Contains(KAgentApiClient.SecretCallRefused, line);
        Assert.Contains("api/a2a/kagent/researcher/", line);
        Assert.DoesNotContain(Secret, line);
    }

    [Fact]
    public async Task AFailedTask_OrAnEmptyAnswer_IsACategory_NotTheModelsText()
    {
        var (failed, _, failedLog) = Create(HttpStatusCode.OK, """{"result":{"kind":"task","status":{"state":"failed","message":{"parts":[{"kind":"text","text":"SECRET-DETAIL"}]}}}}""");
        var (empty, _, _) = Create(HttpStatusCode.OK, """{"result":{"kind":"message","parts":[{"kind":"text","text":"  "}]}}""");

        var a = await Assert.ThrowsAsync<AgentCallException>(() => failed.AskAsync("kagent/a", "p", TestContext.Current.CancellationToken));
        var b = await Assert.ThrowsAsync<AgentCallException>(() => empty.AskAsync("kagent/a", "p", TestContext.Current.CancellationToken));

        Assert.Equal("the agent's task ended 'failed'", a.Message);
        Assert.Equal("the agent answered with no text", b.Message);
        Assert.DoesNotContain(failedLog.Lines, l => l.Contains("SECRET-DETAIL"));
    }

    /// <summary>D556: kagent's own words come back beside the category, cleaned; the client still logs none of it.</summary>
    [Fact]
    public async Task AFailedTask_CarriesKagentsReason_Cleaned_AndTheTask()
    {
        var (client, _, log) = Create(HttpStatusCode.OK,
            "{\"result\":{\"kind\":\"task\",\"id\":\"task-9\",\"status\":{\"state\":\"failed\",\"message\":{\"parts\":[" +
            "{\"kind\":\"text\",\"text\":\"dial tcp: connection refused; Authorization: Bearer " + Secret + "\\n[ERR] forged\"}]}}}}");

        var error = await Assert.ThrowsAsync<AgentCallException>(() => client.AskAsync("kagent/a", "p", TestContext.Current.CancellationToken));

        Assert.Equal("the agent's task ended 'failed'", error.Message);
        Assert.Equal(("task-9", "failed"), (error.TaskId, error.State));
        Assert.StartsWith("dial tcp: connection refused", error.KagentReason);
        Assert.DoesNotContain(Secret, error.KagentReason);
        Assert.DoesNotContain("\n", error.KagentReason);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task AFailedTask_CarriesTheTokensKagentCounted()
    {
        var (client, _, _) = Create(HttpStatusCode.OK, """
            {"result":{"kind":"task","id":"t","status":{"state":"failed"},"metadata":{"kagent_usage_metadata":{"promptTokenCount":100,"candidatesTokenCount":20,"totalTokenCount":120}}}}
            """);

        var error = await Assert.ThrowsAsync<AgentCallException>(() => client.AskAsync("kagent/a", "p", TestContext.Current.CancellationToken));

        Assert.Equal(new ChatUsage(100, 20, 120), error.Usage);
    }

    [Fact]
    public async Task AnApprovalStop_IsNoFailureReason()
    {
        var (client, _, _) = Create(HttpStatusCode.OK, """{"result":{"kind":"task","id":"t","status":{"state":"auth-required","message":{"parts":[{"kind":"text","text":"Sign in at https://idp.example/?state=s3cr3t"}]}}}}""");

        var error = await Assert.ThrowsAsync<AgentCallException>(() => client.AskAsync("kagent/a", "p", TestContext.Current.CancellationToken));

        Assert.Equal("the agent stopped to ask for an approval, which an investigation cannot give", error.Message);
        Assert.Null(error.State);
        Assert.Null(error.KagentReason);
    }
}
