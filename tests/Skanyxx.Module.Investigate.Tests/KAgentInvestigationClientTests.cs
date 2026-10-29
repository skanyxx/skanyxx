using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        Assert.Equal("The cause is X.", answer);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://kagent.test/api/a2a/team-a/writer/", request.RequestUri!.ToString());
        Assert.Equal("0.3", request.Headers.GetValues("A2A-Version").Single());
        Assert.Equal("skanyxx-investigate", request.Headers.GetValues("X-User-Id").Single());
        Assert.Contains("\"method\":\"message/send\"", body);
        Assert.Contains("\"blocking\":true", body);
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
}
