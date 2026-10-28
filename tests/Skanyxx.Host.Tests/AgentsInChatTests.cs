using System.Net.Http.Json;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The agents list says which agents Chat offers (D240). The Tool Servers API: <c>ToolServersApiTests</c>; one agent's
/// details and who may read them: <c>AgentsApiTests</c> (D250, D254).
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AgentsInChatTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        });
        _owner = await _host.OwnerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    /// <summary>Only merged agents are offered in Chat (D100); the Agents page shows Chat by this flag (D240).</summary>
    [Fact]
    public async Task AgentsList_SaysWhichAgentsChatOffers()
    {
        var agents = await _owner.GetFromJsonAsync<JsonElement[]>("/api/agents", Ct);

        bool InChat(string ns, string name) => agents!.Single(a => a.GetProperty("namespace").GetString() == ns && a.GetProperty("name").GetString() == name)
            .GetProperty("inChat").GetBoolean();
        Assert.True(InChat("kagent", "seed"));
        Assert.True(InChat("team-a", "seed"));
        Assert.False(InChat("kagent", "ticket-planner"));
        Assert.False(InChat("kagent", "e2e-hello"));
    }
}
