using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>
/// A kagent controller stand-in on real Kestrel: <c>POST /api/a2a/{ns}/{agent}/</c> answering A2A 0.3 JSON-RPC in
/// the shapes kagent 0.10 produces (completed task with one artifact; failed task with a status message).
/// Replies are scripted per agent; every call is recorded.
/// </summary>
public sealed class FakeKAgent : IAsyncDisposable
{
    public const string Pass = "VERDICT: PASS\n\n## Findings\n1. minor: naming.";
    public const string Fail = "VERDICT: FAIL\n\n## Findings\n1. blocker: no test for the double click.";
    public const string Ship = "RECOMMENDATION: SHIP\n\n## Why\nSmall and tested.";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<AgentReply>> _scripts = new();
    private readonly ConcurrentQueue<AgentCall> _calls = new();

    private FakeKAgent(WebApplication app) => _app = app;

    public string Url => _app.Urls.First();
    public IReadOnlyList<AgentCall> Calls => [.. _calls];

    public IReadOnlyList<AgentCall> CallsTo(string agent) => Calls.Where(c => c.Agent == agent).ToList();

    public static async Task<FakeKAgent> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var fake = new FakeKAgent(app);
        app.MapPost("/api/a2a/{ns}/{agent}/", fake.HandleAsync);
        await app.StartAsync();
        return fake;
    }

    /// <summary>Queue replies for an agent; when the queue is empty the agent's default answer is used.</summary>
    public void Script(string agent, params AgentReply[] replies)
    {
        var queue = _scripts.GetOrAdd(agent, _ => new ConcurrentQueue<AgentReply>());
        foreach (var reply in replies)
            queue.Enqueue(reply);
    }

    private async Task<IResult> HandleAsync(string ns, string agent, HttpContext context)
    {
        var body = (await JsonNode.ParseAsync(context.Request.Body))!;
        var prompt = body["params"]!["message"]!["parts"]![0]!["text"]!.GetValue<string>();
        _calls.Enqueue(new AgentCall(ns, agent, prompt, body["method"]!.GetValue<string>(),
            context.Request.Headers["A2A-Version"], context.Request.Headers["X-User-Id"],
            body["params"]!["configuration"]?["blocking"]?.GetValue<bool>() ?? false));

        var reply = _scripts.TryGetValue(agent, out var queue) && queue.TryDequeue(out var next) ? next : Default(agent);
        switch (reply)
        {
            case AgentReply.Http http:
                return Results.StatusCode(http.Status);
            case AgentReply.Raw raw:
                return Results.Content(raw.Json, "application/json");
            case AgentReply.Blocked blocked:
                await blocked.Release.Task.WaitAsync(context.RequestAborted);
                return Task(body, "completed", blocked.Text);
            case AgentReply.Failed failed:
                return Task(body, "failed", failed.Text);
            case AgentReply.Completed completed:
                return Task(body, "completed", completed.Text);
            default:
                throw new InvalidOperationException(reply.ToString());
        }
    }

    private static AgentReply Default(string agent) => new AgentReply.Completed(agent switch
    {
        "ticket-planner" => "## Understanding\nFix the double charge.\n\n## Approach\n1. Make refund idempotent.",
        "ticket-plan-reviewer" => Ship,
        "ticket-coder" => "## Change summary\nIdempotency key on refund.\n\n## Tests\nA test that clicks twice.",
        "ticket-qa" => Pass,
        "ticket-reviewer" => Ship,
        _ => $"answer from {agent}"
    });

    private static IResult Task(JsonNode request, string state, string text)
    {
        var parts = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = text });
        var result = new JsonObject
        {
            ["kind"] = "task",
            ["id"] = Guid.NewGuid().ToString(),
            ["contextId"] = Guid.NewGuid().ToString(),
            ["status"] = state == "completed"
                ? new JsonObject { ["state"] = state }
                : new JsonObject { ["state"] = state, ["message"] = new JsonObject { ["kind"] = "message", ["role"] = "agent", ["parts"] = parts } },
            ["artifacts"] = state == "completed"
                ? new JsonArray(new JsonObject { ["artifactId"] = Guid.NewGuid().ToString(), ["parts"] = parts })
                : new JsonArray()
        };
        return Results.Content(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = result }
            .ToJsonString(), "application/json");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
