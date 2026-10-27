using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Tests;

/// <summary>
/// C-3: an agent write is audited before kagent is called (fail closed: no audit, no write), and a write kagent applied
/// is never answered as a failure because its outcome row could not be written.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AgentsAuditTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Ns = "kagent";

    private readonly AuditProbe _audit = new();
    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
        }, services =>
        {
            services.AddSingleton(_audit);
            services.AddTransient<IRequestHandler<RecordAuditCommand>, ProbeAuditHandler>();
        });
        _owner = await _host.OwnerAsync();
        _kagent.AgentObjects[$"{Ns}/helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["name"] = "helper", ["namespace"] = Ns,
                ["labels"] = new JsonObject { ["skanyxx.dev/managed-by"] = "skanyxx-agents", ["skanyxx.dev/merged"] = "true" }
            },
            ["spec"] = new JsonObject
            {
                ["type"] = "Declarative",
                ["declarative"] = new JsonObject { ["systemMessage"] = "Answer briefly.", ["modelConfig"] = "default-model-config" }
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutTheIntentRow_NothingReachesKAgent()
    {
        _audit.FailOn.Add("agent.delete_requested");
        _audit.FailOn.Add("agent.create_requested");
        _audit.FailOn.Add("agent.update_requested");

        Assert.Equal(HttpStatusCode.InternalServerError, (await _owner.DeleteAsync($"/api/agents/{Ns}/helper", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await _owner.PostAsJsonAsync("/api/agents", Form("fresh"), Ct)).StatusCode);
        var update = Form("helper");
        update["version"] = (await _owner.GetFromJsonAsync<JsonElement>($"/api/agents/{Ns}/helper", Ct)).GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.InternalServerError, (await _owner.PutAsJsonAsync($"/api/agents/{Ns}/helper", update, Ct)).StatusCode);

        Assert.DoesNotContain(_kagent.Calls, c => c.Method is "POST" or "PUT" or "DELETE" && c.Path.StartsWith("/api/agents", StringComparison.Ordinal));
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/helper"));
    }

    [Fact]
    public async Task AnAppliedWrite_IsAnsweredAsDone_EvenWhenItsOutcomeRowFails_AndItsIntentRowStands()
    {
        _audit.FailOn.Add("agent.deleted");

        Assert.Equal(HttpStatusCode.OK, (await _owner.DeleteAsync($"/api/agents/{Ns}/helper", Ct)).StatusCode);

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/helper"));
        Assert.Contains(_audit.Written, a => a.Action == "agent.delete_requested" && a.TargetId == $"{Ns}/helper");
    }

    [Fact]
    public async Task AWriteKAgentRefuses_IsAuditedAsFailed()
    {
        _kagent.FailAgentWrites = 1;

        Assert.Equal(HttpStatusCode.BadGateway, (await _owner.DeleteAsync($"/api/agents/{Ns}/helper", Ct)).StatusCode);

        Assert.Equal(["agent.delete_requested", "agent.write_failed"],
            _audit.Written.Where(a => a.TargetId == $"{Ns}/helper").Select(a => a.Action));
    }

    private static Dictionary<string, object?> Form(string name) => new()
    {
        ["namespace"] = Ns,
        ["name"] = name,
        ["description"] = "",
        ["systemMessage"] = "Answer briefly.",
        ["modelConfig"] = "default-model-config",
        ["mcpTools"] = Array.Empty<object>(),
        ["agentTools"] = Array.Empty<string>(),
        ["skills"] = Array.Empty<string>(),
        ["a2aSkills"] = Array.Empty<object>()
    };

    /// <summary>Every agent audit row in order; the rows named in <see cref="FailOn"/> fail as a broken audit store would.</summary>
    public sealed class AuditProbe
    {
        public ConcurrentQueue<RecordAuditCommand> Written { get; } = new();

        public ConcurrentBag<string> FailOn { get; } = [];
    }

    private sealed class ProbeAuditHandler(AuditProbe probe) : IRequestHandler<RecordAuditCommand>
    {
        public Task Handle(RecordAuditCommand command, CancellationToken ct)
        {
            if (probe.FailOn.Contains(command.Action))
                throw new InvalidOperationException("synthetic audit store outage");
            probe.Written.Enqueue(command);
            return Task.CompletedTask;
        }
    }
}
