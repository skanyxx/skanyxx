using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>
    /// Chat offers every agent but Skanyxx's own machinery (D557, superseding D100's merged-only rule); the Agents page
    /// shows Chat by this flag (D240), and a suspended agent stays "in Chat" with <c>suspended</c> set (the page says "no (suspended)").
    /// </summary>
    [Fact]
    public async Task AgentsList_SaysWhichAgentsChatOffers()
    {
        Add("from-kubectl", new JsonObject());
        Add("preview-3-helper", new JsonObject { ["skanyxx.dev/preview"] = "3", ["skanyxx.dev/managed-by"] = "skanyxx-studio" });
        Add("skanyxx-factory", new JsonObject { ["skanyxx.dev/role"] = "factory", ["skanyxx.dev/managed-by"] = "skanyxx-studio" });
        Add("asleep", new JsonObject(), suspended: true);
        // Names the seed's memory server, whose secret acts for users: it would be the seed in memory, unreviewed.
        Add("borrower", new JsonObject(), memoryServer: "skanyxx-memory-seed");
        Add("keeper", new JsonObject(), memoryServer: "skanyxx-memory-keeper");
        // Two agents named "twin": only the one that is not Skanyxx's own is offered.
        Add("twin", new JsonObject());
        Add("twin", new JsonObject { ["skanyxx.dev/role"] = "helper" }, ns: "team-b");

        var agents = await _owner.GetFromJsonAsync<JsonElement[]>("/api/agents", Ct);

        bool InChat(string ns, string name) => agents!.Single(a => a.GetProperty("namespace").GetString() == ns && a.GetProperty("name").GetString() == name)
            .GetProperty("inChat").GetBoolean();
        Assert.True(InChat("kagent", "seed"));
        Assert.True(InChat("team-a", "seed"));
        Assert.False(InChat("kagent", "ticket-planner"));
        Assert.True(InChat("kagent", "e2e-hello"));
        Assert.True(InChat("kagent", "from-kubectl"));
        Assert.False(InChat("kagent", "preview-3-helper"));
        Assert.False(InChat("kagent", "skanyxx-factory"));
        Assert.True(InChat("kagent", "asleep"));
        Assert.True(agents!.Single(a => a.GetProperty("name").GetString() == "asleep").GetProperty("suspended").GetBoolean());
        Assert.False(InChat("kagent", "borrower"));
        Assert.True(InChat("kagent", "keeper"));
        Assert.True(InChat("kagent", "twin"));
        Assert.False(InChat("team-b", "twin"));

        var picker = (await _owner.GetFromJsonAsync<JsonElement[]>("/api/chat/agents", Ct))!
            .Select(a => $"{a.GetProperty("namespace").GetString()}/{a.GetProperty("name").GetString()}");
        Assert.Equal(["kagent/e2e-hello", "kagent/from-kubectl", "kagent/keeper", "kagent/seed", "team-a/seed", "kagent/twin"], picker);
    }

    /// <summary>An employee reads (and chats with) only what Chat offers: never the factory, a ticket stage, a preview or a borrower.</summary>
    [Fact]
    public async Task AnEmployee_ReadsOnlyWhatChatOffers()
    {
        Add("from-kubectl", new JsonObject());
        Add("skanyxx-factory", new JsonObject { ["skanyxx.dev/role"] = "factory", ["skanyxx.dev/managed-by"] = "skanyxx-studio" });
        Add("preview-3-helper", new JsonObject { ["skanyxx.dev/preview"] = "3", ["skanyxx.dev/managed-by"] = "skanyxx-studio" });
        Add("borrower", new JsonObject(), memoryServer: "skanyxx-memory-seed");
        Add("twin", new JsonObject { ["skanyxx.dev/role"] = "helper" }, ns: "team-b");
        var employee = await ToolServersApiTests.PersonAsync(_host, _owner, "eli@skanyxx.example", "employee");

        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/api/agents/kagent/from-kubectl", Ct)).StatusCode);
        foreach (var (ns, name) in new[] { ("kagent", "skanyxx-factory"), ("kagent", "ticket-planner"), ("kagent", "preview-3-helper"), ("kagent", "borrower"), ("team-b", "twin") })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/agents/{ns}/{name}", Ct)).StatusCode);
            var turn = await employee.PostAsJsonAsync("/api/chat", new { agentNamespace = ns, agentName = name, message = "hi" }, Ct);
            Assert.Equal(HttpStatusCode.NotFound, turn.StatusCode);
        }
        Assert.Empty(_kagent.CallsTo("POST", "/api/sessions"));
    }

    private void Add(string name, JsonObject labels, bool suspended = false, string? memoryServer = null, string ns = "kagent")
    {
        var declarative = new JsonObject();
        if (suspended)
            declarative["deployment"] = new JsonObject { ["replicas"] = 0 };
        if (memoryServer is not null)
            declarative["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "McpServer", ["mcpServer"] = new JsonObject { ["name"] = memoryServer, ["kind"] = "RemoteMCPServer", ["toolNames"] = new JsonArray("memory_search") }
            });
        _kagent.AgentObjects[$"{ns}/{name}"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = ns, ["labels"] = labels },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = declarative }
        };
    }
}
