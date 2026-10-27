using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skanyxx.Host.Tests;

/// <summary>
/// <c>api/agents</c> (D250–D255) on the real host against <see cref="FakeKAgent"/>: who may do what (every write refused
/// to builders and employees before kagent is called), create/read/update/delete, studio and system agents that never
/// change here, the form's rules, the audit rows, and what a response never carries.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AgentsApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Ns = "kagent";
    private const string Server = "docs-search";

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _builder = null!;
    private HttpClient _employee = null!;

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _kagent.ToolServers[$"{Ns}/{Server}"] = new JsonObject();
        _kagent.DiscoveredTools[$"{Ns}/{Server}"] = ["search", "fetch"];
        var database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(database, s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:McpServers:0"] = Server;
        });
        _owner = await _host.OwnerAsync();
        _supervisor = await PersonAsync("sue@skanyxx.example", "supervisor");
        _builder = await PersonAsync("bo@skanyxx.example", "builder");
        _employee = await PersonAsync("eve@skanyxx.example", "employee");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- Who may do what (D254) ---

    [Fact]
    public async Task EveryEndpoint_RefusesAnonymous_AndEveryWrite_RefusesBuildersAndEmployees_BeforeKAgent()
    {
        Seed("helper", Direct());
        var anonymous = _host.Client();
        var writes = new[] { ("GET", "/api/agents/options"), ("POST", "/api/agents"), ("PUT", $"/api/agents/{Ns}/helper"), ("DELETE", $"/api/agents/{Ns}/helper") };
        foreach (var (method, path) in new[] { ("GET", "/api/agents"), ("GET", $"/api/agents/{Ns}/seed") }.Concat(writes))
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Request(method, path), Ct)).StatusCode);
        var before = KAgentWrites();
        foreach (var client in new[] { _builder, _employee })
            foreach (var (method, path) in writes)
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Request(method, path), Ct)).StatusCode);
        Assert.Equal(before, KAgentWrites());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/helper"));
    }

    [Fact]
    public async Task Employees_ReadOnlyTheAgentsChatOffers_WithoutTheirPrompt_BuildersReadAll()
    {
        Seed("helper", Direct());
        Seed("hidden", External());

        var employeeList = await ListAsync(_employee);
        Assert.All(employeeList, a => Assert.True(a.GetProperty("inChat").GetBoolean()));
        Assert.Contains(employeeList, a => a.GetProperty("name").GetString() == "helper");
        Assert.DoesNotContain(employeeList, a => a.GetProperty("name").GetString() == "hidden");
        Assert.All(employeeList, a => Assert.False(a.GetProperty("canEdit").GetBoolean() || a.GetProperty("canDelete").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, (await _employee.GetAsync($"/api/agents/{Ns}/hidden", Ct)).StatusCode);
        var asEmployee = await _employee.GetFromJsonAsync<JsonElement>($"/api/agents/{Ns}/helper", Ct);
        Assert.Equal(JsonValueKind.Null, asEmployee.GetProperty("systemMessage").ValueKind);

        Assert.Contains(await ListAsync(_builder), a => a.GetProperty("name").GetString() == "hidden");
        var asBuilder = await _builder.GetFromJsonAsync<JsonElement>($"/api/agents/{Ns}/helper", Ct);
        Assert.Equal("Answer briefly.", asBuilder.GetProperty("systemMessage").GetString());
        Assert.False(asBuilder.GetProperty("canEdit").GetBoolean());
    }

    // --- Create, read, update, delete ---

    [Fact]
    public async Task Supervisor_CreatesReadsUpdatesAndDeletes_AndEveryWriteIsAudited()
    {
        var options = await _supervisor.GetFromJsonAsync<JsonElement>("/api/agents/options", Ct);
        Assert.Contains("default-model-config", options.GetProperty("modelConfigs").EnumerateArray().Select(m => m.GetString()));
        Assert.Contains(options.GetProperty("mcpServers").EnumerateArray(), s => s.GetProperty("server").GetString() == Server);

        var created = await _supervisor.PostAsJsonAsync("/api/agents", Form("crud-agent"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stored = _kagent.AgentObjects[$"{Ns}/crud-agent"];
        Assert.Equal("skanyxx-agents", stored["metadata"]!["labels"]!["skanyxx.dev/managed-by"]!.GetValue<string>());
        Assert.Equal("true", stored["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());
        Assert.Equal("Declarative", stored["spec"]!["type"]!.GetValue<string>());
        Assert.Equal("search", stored["spec"]!["declarative"]!["tools"]![0]!["mcpServer"]!["toolNames"]![0]!.GetValue<string>());
        Assert.Equal("e2e-hello", stored["spec"]!["declarative"]!["tools"]![1]!["agent"]!["name"]!.GetValue<string>());
        Assert.Equal("summarize", stored["spec"]!["declarative"]!["a2aConfig"]!["skills"]![0]!["id"]!.GetValue<string>());

        var read = await _employee.GetFromJsonAsync<JsonElement>($"/api/agents/{Ns}/crud-agent", Ct);
        Assert.Equal("Summarizes docs.", read.GetProperty("description").GetString());
        Assert.Equal("direct", read.GetProperty("source").GetString());

        // A field the form does not own (set in kagent-ui or kubectl) survives an edit; the body cannot rename the agent.
        stored["spec"]!["declarative"]!["deployment"] = new JsonObject { ["replicas"] = 2 };
        var update = Form("someone-else", prompt: "Summarize in three bullets.", tools: [new { server = Server, tools = new[] { "fetch" } }]);
        var updated = await _owner.PutAsJsonAsync($"/api/agents/{Ns}/crud-agent", update, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        stored = _kagent.AgentObjects[$"{Ns}/crud-agent"];
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/someone-else"));
        Assert.Equal("Summarize in three bullets.", stored["spec"]!["declarative"]!["systemMessage"]!.GetValue<string>());
        Assert.Equal(2, stored["spec"]!["declarative"]!["deployment"]!["replicas"]!.GetValue<int>());
        Assert.Single(stored["spec"]!["declarative"]!["tools"]!.AsArray());
        Assert.Equal("true", stored["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.OK, (await _supervisor.DeleteAsync($"/api/agents/{Ns}/crud-agent", Ct)).StatusCode);
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/crud-agent"));
        Assert.Equal(HttpStatusCode.NotFound, (await _supervisor.GetAsync($"/api/agents/{Ns}/crud-agent", Ct)).StatusCode);

        foreach (var action in new[] { "agent.created", "agent.updated", "agent.deleted" })
        {
            var rows = await _owner.GetFromJsonAsync<JsonElement>($"/api/identity/audit?action={action}", Ct);
            var row = Assert.Single(rows.EnumerateArray());
            Assert.Equal($"{Ns}/crud-agent", row.GetProperty("targetId").GetString());
            Assert.DoesNotContain("Summarize", row.ToString());
        }
    }

    [Fact]
    public async Task ACreate_OfATakenName_IsAConflict()
    {
        Seed("helper", Direct());
        var response = await _supervisor.PostAsJsonAsync("/api/agents", Form("helper"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- Studio and system agents (D252) ---

    [Fact]
    public async Task StudioAndSystemAgents_AreNeverEditedOrDeletedHere_AndTheRefusalIsAudited()
    {
        Seed("refund-helper", new JsonObject { ["skanyxx.dev/managed-by"] = "skanyxx-studio", ["skanyxx.dev/merged"] = "true" });
        Seed("skanyxx-factory", new JsonObject { ["skanyxx.dev/managed-by"] = "skanyxx-studio", ["skanyxx.dev/role"] = "factory" });
        var before = KAgentWrites();

        var studioEdit = await _owner.PutAsJsonAsync($"/api/agents/{Ns}/refund-helper", Form("refund-helper"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, studioEdit.StatusCode);
        Assert.Contains("Studio", await studioEdit.Content.ReadAsStringAsync(Ct));
        Assert.Contains("agents/refund-helper/agent.yaml", await studioEdit.Content.ReadAsStringAsync(Ct));
        foreach (var name in new[] { "refund-helper", "skanyxx-factory", "seed", "ticket-planner" })
            Assert.Equal(HttpStatusCode.Conflict, (await _owner.DeleteAsync($"/api/agents/{Ns}/{name}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _supervisor.PutAsJsonAsync($"/api/agents/{Ns}/seed", Form("seed"), Ct)).StatusCode);

        Assert.Equal(before, KAgentWrites());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper") && _kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory"));
        var list = await ListAsync(_owner);
        foreach (var name in new[] { "refund-helper", "skanyxx-factory", "seed", "ticket-planner" })
        {
            var row = list.First(a => a.GetProperty("name").GetString() == name);
            Assert.False(row.GetProperty("canEdit").GetBoolean() || row.GetProperty("canDelete").GetBoolean());
            Assert.False(string.IsNullOrEmpty(row.GetProperty("protection").GetString()));
        }
        var refused = await _owner.GetFromJsonAsync<JsonElement>("/api/identity/audit?action=agent.write_refused", Ct);
        Assert.Equal(6, refused.GetArrayLength());
    }

    // --- The form's rules (D251) ---

    [Theory]
    [InlineData("Bad_Name", null, null, null)]
    [InlineData("skanyxx-mine", null, null, null)]
    [InlineData("ok-name", "other-ns", null, null)]
    [InlineData("ok-name", null, "not-allowed", null)]
    [InlineData("ok-name", null, null, "missing-model")]
    public async Task AFormBreakingARule_IsA400_AndNeverReachesKAgent(string name, string? ns, string? server, string? model)
    {
        var form = Form(name, tools: server is null ? null : [new { server, tools = new[] { "x" } }]);
        form["namespace"] = ns ?? Ns;
        if (model is not null)
            form["modelConfig"] = model;
        var before = KAgentWrites();
        var response = await _supervisor.PostAsJsonAsync("/api/agents", form, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, KAgentWrites());
    }

    [Fact]
    public async Task OversizedPrompts_UnpinnedSkills_AndBadA2aSkills_AreRefused()
    {
        var skilled = Form("skilled");
        skilled["skills"] = new[] { "ghcr.io/acme/skill:latest" };
        var a2a = Form("a2a");
        a2a["a2aSkills"] = new[] { new { id = "bad id!", name = "" } };
        foreach (var form in new[] { Form("big", prompt: new string('x', 16_001)), skilled, a2a })
            Assert.Equal(HttpStatusCode.BadRequest, (await _supervisor.PostAsJsonAsync("/api/agents", form, Ct)).StatusCode);
        Assert.Empty(_kagent.CallsTo("POST", "/api/agents"));
    }

    [Fact]
    public async Task AnAgentsExistingTools_MayStayAsTheyAre_WithTheirHeaders_ButNeverWiden()
    {
        Seed("legacy", External(), tools: new JsonArray(
            new JsonObject
            {
                ["type"] = "McpServer",
                ["headersFrom"] = new JsonArray(new JsonObject { ["name"] = "Authorization", ["valueFrom"] = new JsonObject { ["type"] = "Secret", ["name"] = "legacy-token", ["key"] = "token" } }),
                ["mcpServer"] = new JsonObject { ["name"] = "legacy-mcp", ["toolNames"] = new JsonArray("a"), ["allowedHeaders"] = new JsonArray("x-user-id") }
            },
            new JsonObject { ["type"] = "Agent", ["isolateSessions"] = true, ["agent"] = new JsonObject { ["name"] = "seed" } }));

        var form = Form("legacy", tools: [new { server = "legacy-mcp", tools = new[] { "a" } }]);
        form["agentTools"] = new[] { "seed" };
        var kept = await _supervisor.PutAsJsonAsync($"/api/agents/{Ns}/legacy", form, Ct);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        var tools = _kagent.AgentObjects[$"{Ns}/legacy"]["spec"]!["declarative"]!["tools"]!;
        Assert.Equal("legacy-token", tools[0]!["headersFrom"]![0]!["valueFrom"]!["name"]!.GetValue<string>());
        Assert.Equal("x-user-id", tools[0]!["mcpServer"]!["allowedHeaders"]![0]!.GetValue<string>());
        Assert.True(tools[1]!["isolateSessions"]!.GetValue<bool>());

        var widened = await _supervisor.PutAsJsonAsync($"/api/agents/{Ns}/legacy", Form("legacy", tools: [new { server = "legacy-mcp", tools = new[] { "a", "b" } }]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, widened.StatusCode);
    }

    [Fact]
    public async Task SystemAndStudioAgents_AreNeverToolsOfANewAgent()
    {
        Seed("refund-helper", new JsonObject { ["skanyxx.dev/managed-by"] = "skanyxx-studio", ["skanyxx.dev/merged"] = "true" });
        var options = await _supervisor.GetFromJsonAsync<JsonElement>("/api/agents/options", Ct);
        Assert.Equal(["e2e-hello"], options.GetProperty("agents").EnumerateArray().Select(a => a.GetString()));
        foreach (var target in new[] { "seed", "refund-helper", "ticket-planner" })
        {
            var form = Form("caller");
            form["agentTools"] = new[] { target };
            Assert.Equal(HttpStatusCode.BadRequest, (await _supervisor.PostAsJsonAsync("/api/agents", form, Ct)).StatusCode);
        }
        Assert.Empty(_kagent.CallsTo("POST", "/api/agents"));
    }

    [Fact]
    public async Task AgentsOutsideTheStudioNamespace_AreNeverEditedOrDeleted()
    {
        _kagent.AgentObjects["other-ns/elsewhere"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "elsewhere", ["namespace"] = "other-ns", ["labels"] = External() },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["systemMessage"] = "x" } }
        };
        var row = (await ListAsync(_owner)).First(a => a.GetProperty("name").GetString() == "elsewhere");
        Assert.False(row.GetProperty("canEdit").GetBoolean() || row.GetProperty("canDelete").GetBoolean());
        var form = Form("elsewhere");
        form["namespace"] = "other-ns";
        Assert.Equal(HttpStatusCode.Conflict, (await _owner.PutAsJsonAsync("/api/agents/other-ns/elsewhere", form, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _owner.DeleteAsync("/api/agents/other-ns/elsewhere", Ct)).StatusCode);
        Assert.True(_kagent.AgentObjects.ContainsKey("other-ns/elsewhere"));
    }

    /// <summary>C-1: two entries for one server, each with its own Secret header, are never merged by a save.</summary>
    [Fact]
    public async Task AnAgentNamingOneServerTwice_IsNeverEdited_SoNoCredentialMoves()
    {
        static JsonObject Entry(string tool, string secret) => new()
        {
            ["type"] = "McpServer",
            ["headersFrom"] = new JsonArray(new JsonObject { ["name"] = "Authorization", ["valueFrom"] = new JsonObject { ["type"] = "Secret", ["name"] = secret, ["key"] = "token" } }),
            ["mcpServer"] = new JsonObject { ["name"] = Server, ["toolNames"] = new JsonArray(tool) }
        };
        Seed("dup", External(), tools: new JsonArray(Entry("search", "token-a"), Entry("fetch", "token-b")));
        var before = _kagent.AgentObjects[$"{Ns}/dup"].ToJsonString();

        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/agents/{Ns}/dup", Ct);
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        // What the page would send for it: one entry for the server (the form names each server once).
        var saved = await _supervisor.PutAsJsonAsync($"/api/agents/{Ns}/dup", Form("dup", tools: [new { server = Server, tools = new[] { "fetch" } }]), Ct);

        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Empty(_kagent.CallsTo("PUT", "/api/agents"));
        Assert.Equal(before, _kagent.AgentObjects[$"{Ns}/dup"].ToJsonString());
    }

    [Fact]
    public async Task Responses_NeverCarryAnnotationsHeadersOrEnv()
    {
        Seed("helper", Direct());
        var stored = _kagent.AgentObjects[$"{Ns}/helper"];
        stored["metadata"]!["annotations"] = new JsonObject { ["kubectl.kubernetes.io/last-applied-configuration"] = "SECRET-ANNOTATION" };
        stored["spec"]!["declarative"]!["deployment"] = new JsonObject { ["env"] = new JsonArray(new JsonObject { ["name"] = "TOKEN", ["value"] = "SECRET-ENV" }) };
        stored["spec"]!["declarative"]!["tools"] = new JsonArray(new JsonObject
        {
            ["type"] = "McpServer",
            ["mcpServer"] = new JsonObject { ["name"] = Server, ["headersFrom"] = new JsonArray(new JsonObject { ["name"] = "Authorization", ["value"] = "SECRET-HEADER" }) }
        });

        var body = await _owner.GetStringAsync($"/api/agents/{Ns}/helper", Ct) + await _owner.GetStringAsync("/api/agents", Ct);
        Assert.DoesNotContain("SECRET", body);
    }

    private static JsonObject Direct() => new() { ["skanyxx.dev/managed-by"] = "skanyxx-agents", ["skanyxx.dev/merged"] = "true" };

    private static JsonObject External() => new() { ["created-by"] = "kagent-ui" };

    private void Seed(string name, JsonObject labels, JsonArray? tools = null) =>
        _kagent.AgentObjects[$"{Ns}/{name}"] = new JsonObject
        {
            ["apiVersion"] = "kagent.dev/v1alpha2",
            ["kind"] = "Agent",
            ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = Ns, ["labels"] = labels },
            ["spec"] = new JsonObject
            {
                ["type"] = "Declarative",
                ["description"] = $"{name} agent",
                ["declarative"] = new JsonObject { ["systemMessage"] = "Answer briefly.", ["modelConfig"] = "default-model-config", ["tools"] = tools }
            }
        };

    private static Dictionary<string, object?> Form(string name, string prompt = "Summarize the docs you are given.", object[]? tools = null) => new()
    {
        ["namespace"] = Ns,
        ["name"] = name,
        ["description"] = "Summarizes docs.",
        ["systemMessage"] = prompt,
        ["modelConfig"] = "default-model-config",
        ["runtime"] = "go",
        ["stream"] = true,
        ["mcpTools"] = tools ?? [new { server = Server, tools = new[] { "search" } }],
        ["agentTools"] = tools is null ? new[] { "e2e-hello" } : [],
        ["skills"] = Array.Empty<string>(),
        ["a2aSkills"] = tools is null ? new[] { new { id = "summarize", name = "Summarize", description = "Short summaries", tags = new[] { "docs" }, examples = new[] { "Summarize this" } } } : []
    };

    private int KAgentWrites() =>
        _kagent.Calls.Count(c => c.Method is "POST" or "PUT" or "DELETE" && c.Path.StartsWith("/api/agents", StringComparison.Ordinal));

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path) { Content = method is "POST" or "PUT" ? JsonContent.Create(new { name = "x" }) : null };

    private static async Task<List<JsonElement>> ListAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>("/api/agents", Ct)).EnumerateArray()];

    private async Task<HttpClient> PersonAsync(string email, string role)
    {
        var invite = await _owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return _host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }
}
