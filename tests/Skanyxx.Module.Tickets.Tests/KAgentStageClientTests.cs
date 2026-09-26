using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class KAgentStageClientTests
{
    private static readonly StageAgent Agent = new("qa", "team-a/ticket-qa");

    private static (KAgentStageClient Client, FakeHandler Http) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new FakeHandler(respond);
        var client = new KAgentStageClient(new HttpClient(http) { BaseAddress = new Uri("http://kagent:8083/") },
            Options.Create(new TicketsOptions { KAgentUserId = "svc-tickets" }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<KAgentStageClient>.Instance);
        return (client, http);
    }

    private static HttpResponseMessage Result(string resultJson) =>
        FakeHandler.Json($$"""{"jsonrpc":"2.0","id":"1","result":{{resultJson}}}""");

    [Fact]
    public async Task Sends_A2A03_MessageSend_ToTheAgentsPath()
    {
        var (client, http) = Create(_ => Result("""{"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[{"kind":"text","text":"ok"}]}]}"""));

        await client.AskAsync(Agent, "the prompt", CancellationToken.None);

        var request = http.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://kagent:8083/api/a2a/team-a/ticket-qa/", request.RequestUri!.ToString());
        Assert.Equal("0.3", request.Headers.GetValues("A2A-Version").Single());
        Assert.Equal("svc-tickets", request.Headers.GetValues("X-User-Id").Single());
        Assert.NotNull(request.Content!.Headers.ContentLength);
        var body = JsonNode.Parse(http.Bodies.Single()!)!;
        Assert.Equal("message/send", body["method"]!.GetValue<string>());
        Assert.Equal("user", body["params"]!["message"]!["role"]!.GetValue<string>());
        Assert.Equal("the prompt", body["params"]!["message"]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.True(body["params"]!["configuration"]!["blocking"]!.GetValue<bool>());
        Assert.Null(body["params"]!["message"]!["contextId"]);
    }

    [Theory]
    [InlineData("""{"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[{"kind":"text","text":"old"}]},{"parts":[{"kind":"data","data":{}},{"kind":"text","text":"new "},{"kind":"text","text":"answer"}]}]}""", "new answer")]
    [InlineData("""{"kind":"task","status":{"state":"completed","message":{"parts":[{"kind":"text","text":"from status"}]}},"artifacts":[]}""", "from status")]
    [InlineData("""{"kind":"message","role":"agent","parts":[{"kind":"text","text":"direct"}]}""", "direct")]
    public async Task ReadsTheAnswer(string result, string expected)
    {
        var (client, _) = Create(_ => Result(result));

        Assert.Equal(expected, await client.AskAsync(Agent, "p", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"kind":"task","status":{"state":"failed","message":{"parts":[{"kind":"text","text":"LLM error: 500 boom at internal-host:9000"}]}}}""", "the agent's task ended 'failed'")]
    [InlineData("""{"kind":"task","status":{"state":"input-required"}}""", "the agent's task ended 'input-required'")]
    [InlineData("""{"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[{"kind":"text","text":"  "}]}]}""", "the agent answered with no text")]
    public async Task NonAnswers_AreStageAgentErrors(string result, string expected)
    {
        var (client, _) = Create(_ => Result(result));

        var ex = await Assert.ThrowsAsync<StageAgentException>(() => client.AskAsync(Agent, "p", CancellationToken.None));

        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public async Task JsonRpcError_IsAStageAgentError()
    {
        var (client, _) = Create(_ => FakeHandler.Json("""{"jsonrpc":"2.0","id":"1","error":{"code":-32601,"message":"Method not found"}}"""));

        var ex = await Assert.ThrowsAsync<StageAgentException>(() => client.AskAsync(Agent, "p", CancellationToken.None));

        Assert.Equal("kagent refused the call (JSON-RPC -32601)", ex.Message);
    }

    /// <summary>Off-schema bodies must reach the fallback, not escape as an exception that stalls the worker.</summary>
    [Theory]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":1}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":"task","status":{"state":"completed"},"artifacts":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":"task","status":{"state":"completed"},"artifacts":[null]}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":"task","status":{"state":7}}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":{"kind":"task"}}""")]
    [InlineData("""{"jsonrpc":"2.0","result":"nope"}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("""{"jsonrpc":"2.0","error":{"code":"not-a-number","message":"x"}}""")]
    public async Task UnexpectedShapes_AreStageAgentErrors(string json)
    {
        var (client, _) = Create(_ => FakeHandler.Json(json));

        var ex = await Assert.ThrowsAsync<StageAgentException>(() => client.AskAsync(Agent, "p", CancellationToken.None));

        Assert.Equal("kagent answered in an unexpected shape", ex.Message);
    }

    [Theory]
    [InlineData("kagent_thought")]
    [InlineData("adk_thought")]
    public async Task ReasoningParts_AreNotTheAnswer(string marker)
    {
        var (client, _) = Create(_ => Result($$$"""
            {"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[
              {"kind":"text","text":"VERDICT: PASS\n(draft; checking the tests next)\n","metadata":{"{{{marker}}}":true}},
              {"kind":"text","text":"VERDICT: FAIL\n\n## Findings\n1. blocker"}]}]}
            """));

        var answer = await client.AskAsync(Agent, "p", CancellationToken.None);

        Assert.StartsWith("VERDICT: FAIL", answer);
        Assert.Equal(Skanyxx.Module.Tickets.Domain.Verdict.Fail, VerdictReader.Read(Skanyxx.Module.Tickets.Domain.StageKind.Qa, answer));
    }

    [Fact]
    public async Task NulCharacters_AreRemovedFromTheAnswer()
    {
        var (client, _) = Create(_ => Result("""{"kind":"task","status":{"state":"completed"},"artifacts":[{"parts":[{"kind":"text","text":"a\u0000b"}]}]}"""));

        Assert.Equal("ab", await client.AskAsync(Agent, "p", CancellationToken.None));
    }

    [Fact]
    public async Task HttpErrorsAndOutages_NameNoUrl()
    {
        var (notFound, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var (down, _) = Create(_ => throw new HttpRequestException("Connection refused (kagent:8083) http://secret-host/"));

        var a = await Assert.ThrowsAsync<StageAgentException>(() => notFound.AskAsync(Agent, "p", CancellationToken.None));
        var b = await Assert.ThrowsAsync<StageAgentException>(() => down.AskAsync(Agent, "p", CancellationToken.None));

        Assert.Equal("kagent answered HTTP 404", a.Message);
        Assert.Equal("kagent is unreachable (HttpRequestException)", b.Message);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var (client, _) = Create(_ => throw new TaskCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AskAsync(Agent, "p", cts.Token));
    }
}
