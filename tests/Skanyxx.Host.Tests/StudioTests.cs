using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Slice 3 (todo.md): Studio → pull request in skanyxx-agents → only a supervisor's merge makes the agent live in kagent,
/// labelled merged, with its grants in memory (D020–D024, D028–D033, D045). On the real host against
/// <see cref="FakeGitea"/> and <see cref="FakeKAgent"/>. Roles: owner, supervisor, builder, employee.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class StudioTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Ns = "kagent";

    private FakeGitea _git = null!;
    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _builder = null!;
    private HttpClient _builder2 = null!;
    private HttpClient _employee = null!;
    private string _builderId = null!;
    private string _supervisorId = null!;
    private StringWriter _console = null!;
    private TextWriter _originalOut = null!;

    public async ValueTask InitializeAsync()
    {
        _originalOut = Console.Out;
        _console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(_console));
        _git = await FakeGitea.StartAsync();
        _kagent = await FakeKAgent.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = _git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:McpServers:0"] = "jira";
            s["Studio:SkillRegistries:0"] = "ghcr.io/acme/skills/";
            s["Studio:ReconcileSeconds"] = "3600";
            s["Studio:LockWaitSeconds"] = "2";
        });
        _owner = await _host.OwnerAsync();
        // Setup only wakes the reconciler (m13); its first pass creates and records the repo, then keeps the factory.
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory"), "the first reconcile pass after setup");
        (_supervisor, _supervisorId) = await PersonAsync("sue@skanyxx.example", "supervisor");
        (_builder, _builderId) = await PersonAsync("bo@skanyxx.example", "builder");
        (_builder2, _) = await PersonAsync("bea@skanyxx.example", "builder");
        (_employee, _) = await PersonAsync("eve@skanyxx.example", "employee");
        Created(await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }));
        Created(await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" }));
    }

    public async ValueTask DisposeAsync()
    {
        Console.SetOut(_originalOut);
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
        await _git.DisposeAsync();
    }

    private static object Draft(string name = "refund-helper", object[]? grants = null, int? ttl = null, object[]? tools = null) => new
    {
        name,
        description = "Answers refund questions",
        modelConfig = "default-model-config",
        instructions = "Answer refund questions from company memory.\nNever promise a refund outside the policy.",
        skills = Array.Empty<string>(),
        mcpTools = tools ?? [],
        grants = grants ?? [new { scope = "company", search = true, upsert = false }],
        memoryTtlDays = ttl
    };

    private async Task<int> ProposeAsync(HttpClient who, object draft)
    {
        var response = await who.PostAsJsonAsync("/api/studio/proposals", draft);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetInt32();
    }

    private Task<HttpResponseMessage> MergeAsync(HttpClient who, int number) => who.PostAsync($"/api/studio/proposals/{number}/merge", null);

    // The reconciler is internal to the agents module, which the host loaded into its own load context: reach it through
    // the module's hosted loop, whose assembly is that copy.
    private async Task<int> ReconcileAsync()
    {
        var loop = _host.Services.GetServices<IHostedService>().Single(h => h.GetType().Name == "StudioReconcileLoop");
        var type = loop.GetType().Assembly.GetType("Skanyxx.Module.Agents.Studio.Reconcile.StudioReconciler")!;
        var reconciler = _host.Services.GetRequiredService(type);
        return await (Task<int>)type.GetMethod("ReconcileAsync")!.Invoke(reconciler, [CancellationToken.None])!;
    }

    // --- Setup creates the repo (D023) ---

    [Fact]
    public async Task TheRepo_IsCreated_Private_WithMainProtected_SoOnlyAMergeChangesIt()
    {
        Assert.Contains("skanyxx", _git.Orgs);
        Assert.True(_git.Repos.ContainsKey("skanyxx/skanyxx-agents"));
        var rule = Assert.Single(_git.Protections);
        Assert.Equal("main", rule["rule_name"]!.GetValue<string>());
        Assert.False(rule["enable_push"]!.GetValue<bool>());
        Assert.True(rule["enable_merge_whitelist"]!.GetValue<bool>());
        Assert.Equal(["skanyxx-bot"], rule["merge_whitelist_usernames"]!.AsArray().Select(u => u!.GetValue<string>()));
    }

    [Fact]
    public async Task TheRepo_IsCreatedBySetup_NotByABareStart()
    {
        await using var git = await FakeGitea.StartAsync();
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:ReconcileSeconds"] = "3600";
        });
        Assert.Empty(git.Repos);

        (await host.BootstrapAsync()).EnsureSuccessStatusCode();

        await WaitUntilAsync(() => git.Protections.Count == 1, "the repo setup asked for");
        Assert.True(git.Repos.ContainsKey("skanyxx/skanyxx-agents"));
    }

    [Fact]
    public async Task Setup_IsNotBlockedByGit()
    {
        await using var git = await FakeGitea.StartAsync();
        git.Down = true;
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
        });

        Assert.Equal(HttpStatusCode.Created, (await host.BootstrapAsync()).StatusCode);
        Assert.Empty(git.Repos);
    }

    // --- Permission matrix (D022, D024, D030, D031) ---

    [Fact]
    public async Task Employees_GetNoStudio_NoFactory_NoPreview_AndNoMerge()
    {
        var number = await ProposeAsync(_builder, Draft());

        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.GetAsync("/api/studio", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.GetAsync("/api/studio/options", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/studio/proposals", Draft("other"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/studio/proposals", Draft("seed"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.GetAsync($"/api/studio/proposals/{number}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/studio/factory", new { request = "make an agent" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsync($"/api/studio/proposals/{number}/preview", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync($"/api/studio/proposals/{number}/chat", new { message = "hi" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MergeAsync(_employee, number)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsync($"/api/studio/proposals/{number}/close", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/studio", TestContext.Current.CancellationToken)).StatusCode);
        Assert.DoesNotContain(_kagent.AgentObjects.Keys, k => k.Contains("refund"));
    }

    [Fact]
    public async Task ABuilder_Proposes_ButCannotMerge_ASupervisorMerges_AndItIsLive()
    {
        var number = await ProposeAsync(_builder, Draft());
        var builderMerge = await MergeAsync(_builder, number);
        Assert.Equal(HttpStatusCode.Forbidden, builderMerge.StatusCode);
        Assert.Contains("Only a supervisor", await builderMerge.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(_git.Pulls.Single().Open);
        Assert.Empty(StudioAgents());
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
        Assert.DoesNotContain("refund-helper", (await _employee.GetStringAsync("/api/chat/agents", TestContext.Current.CancellationToken)));

        var merged = await MergeAsync(_supervisor, number);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        Assert.False(_git.Pulls.Single().Open);
        Assert.True(_git.Main().ContainsKey("agents/refund-helper/agent.yaml"));
        var agent = _kagent.AgentObjects[$"{Ns}/refund-helper"];
        Assert.Equal("true", agent["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());
        Assert.Equal("skanyxx-studio", agent["metadata"]!["labels"]!["skanyxx.dev/managed-by"]!.GetValue<string>());
        var tool = agent["spec"]!["declarative"]!["tools"]![0]!["mcpServer"]!;
        Assert.Equal("skanyxx-memory-refund-helper", tool["name"]!.GetValue<string>());
        Assert.Equal(["memory_search"], tool["toolNames"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Null(tool["allowedHeaders"]); // a studio agent never acts for users (D084)
        var server = _kagent.ToolServers[$"{Ns}/skanyxx-memory-refund-helper"];
        Assert.Equal("http://skanyxx.skanyxx.svc:8080/mcp/memory", server["spec"]!["url"]!.GetValue<string>());
        // An employee now talks to it in Chat (D031).
        var chatAgents = await _employee.GetFromJsonAsync<JsonElement>("/api/chat/agents", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(chatAgents.EnumerateArray(), a => a.GetProperty("name").GetString() == "refund-helper");
        var answer = await _employee.PostAsJsonAsync("/api/chat", new { message = "refund window?", agentNamespace = Ns, agentName = "refund-helper" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }

    [Fact]
    public async Task TheMergedAgentsSecret_SearchesItsGrant_AndCannotUpsert()
    {
        (await _owner.PutAsJsonAsync("/api/memory/cards/company/refund-window",
            new { version = 0, type = "fact", what = "Refunds within 14 days", why = "Finance policy", body = (string?)null, source = (string?)null }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, await ProposeAsync(_builder, Draft()))).StatusCode);

        var header = Assert.Single(_kagent.Secrets).Value.Value;
        Assert.StartsWith("Bearer skx_mem_", header);
        await using var mcp = await McpAsync(header["Bearer ".Length..], userId: "someone");
        var found = await mcp.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: TestContext.Current.CancellationToken);
        var upsert = await mcp.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "draft", ["type"] = "fact", ["what"] = "x", ["why"] = "y", ["version"] = 0, ["scope"] = "company"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Refunds within 14 days", Text(found));
        Assert.True(upsert.IsError);
        var grants = await _owner.GetFromJsonAsync<JsonElement>("/api/memory/grants/refund-helper", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("""[{"scope":"company","canSearch":true,"canUpsert":false}]""", grants.GetRawText());
        Assert.Equal("studio", (await _owner.GetFromJsonAsync<JsonElement>("/api/memory/agents/refund-helper/secret", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("createdBy").GetString());
    }

    // --- PR content: the same PR carries agent.yaml and grants.yaml (D029) ---

    [Fact]
    public async Task AProposal_IsOneCommit_WithBothFiles_InTheBuildersName_AndTouchesNothingLive()
    {
        var number = await ProposeAsync(_builder, Draft(ttl: 7));

        var commit = Assert.Single(_git.Calls, c => c.Method == "POST" && c.Path.EndsWith("/contents"));
        Assert.Equal("main", commit.Body!["branch"]!.GetValue<string>());
        Assert.StartsWith("studio/refund-helper-", commit.Body["new_branch"]!.GetValue<string>());
        Assert.Equal("bo@skanyxx.example", commit.Body["author"]!["email"]!.GetValue<string>());
        Assert.Equal(["agents/refund-helper/agent.yaml", "agents/refund-helper/grants.yaml"],
            commit.Body["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()));
        var pull = _git.Pulls.Single(p => p.Number == number);
        Assert.Contains($"Proposed-by: {_builderId}", pull.Body);
        var files = _git.Repos["skanyxx/skanyxx-agents"][pull.Head];
        Assert.Equal("""
            # Skanyxx studio. Applied to kagent by Skanyxx's reconciler when a supervisor merges (D045).
            apiVersion: kagent.dev/v1alpha2
            kind: Agent
            metadata:
              name: refund-helper
              namespace: kagent
            spec:
              type: Declarative
              description: Answers refund questions
              declarative:
                modelConfig: default-model-config
                systemMessage: |-
                  Answer refund questions from company memory.
                  Never promise a refund outside the policy.
                tools:
                - type: McpServer
                  mcpServer:
                    name: skanyxx-memory-refund-helper
                    kind: RemoteMCPServer
                    apiGroup: kagent.dev
                    toolNames:
                    - memory_search
                memory:
                  modelConfig: default-model-config
                  ttlDays: 7

            """.Replace("\r\n", "\n"), files["agents/refund-helper/agent.yaml"]);
        Assert.Contains("agent: refund-helper", files["agents/refund-helper/grants.yaml"]);
        Assert.Contains("- scope: company\n  search: true\n  upsert: false", files["agents/refund-helper/grants.yaml"]);
        Assert.False(_git.Main().ContainsKey("agents/refund-helper/agent.yaml"));
        Assert.Empty(StudioAgents());
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
    }

    [Theory]
    [InlineData("seed", HttpStatusCode.BadRequest)]           // reserved
    [InlineData("preview-1-x", HttpStatusCode.BadRequest)]    // reserved prefix
    [InlineData("ticket-planner", HttpStatusCode.Conflict)]   // kagent runs it, not the studio's
    [InlineData("Bad Name", HttpStatusCode.BadRequest)]
    public async Task ANameTheStudioDoesNotOwn_IsRefused(string name, HttpStatusCode expected)
    {
        var response = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft(name), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(_git.Pulls);
    }

    [Fact]
    public async Task TheFormIsChecked_PersonalScopes_UnknownMcpServers_AndBadModels_AreRefused()
    {
        var personal = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft(grants: [new { scope = "personal", search = true, upsert = true }]), cancellationToken: TestContext.Current.CancellationToken);
        var unlisted = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft(tools: [new { server = "skanyxx-memory-seed", tools = new[] { "memory_upsert" } }]), cancellationToken: TestContext.Current.CancellationToken);
        var model = await _builder.PostAsJsonAsync("/api/studio/proposals", new
        {
            name = "x1", description = "", modelConfig = "missing-model", instructions = "x", skills = Array.Empty<string>(),
            mcpTools = Array.Empty<object>(), grants = Array.Empty<object>(), memoryTtlDays = (int?)null
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, personal.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unlisted.StatusCode);
        Assert.Contains("not on the allow-list", await unlisted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, model.StatusCode);
        Assert.Empty(_git.Pulls);
    }

    [Fact]
    public async Task OneOpenProposalPerAgent()
    {
        await ProposeAsync(_builder, Draft());

        var second = await _builder2.PostAsJsonAsync("/api/studio/proposals", Draft(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("pending", (await second.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
    }

    // --- Merge-time validation of what is really in the branch ---

    public static TheoryData<string, Dictionary<string, string?>> Tampered => new()
    {
        { "a ConfigMap instead of an Agent", new() { ["agents/refund-helper/agent.yaml"] = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: refund-helper\n" } },
        { "a BYO image", new() { ["agents/refund-helper/agent.yaml"] = null } },
        { "an extra file", new() { ["agents/refund-helper/run.sh"] = "curl evil" } },
        { "a second agent", new() { ["agents/other/agent.yaml"] = "x" } },
        { "grants not matching the tools", new() { ["agents/refund-helper/grants.yaml"] = "agent: refund-helper\ngrants:\n- scope: company\n  search: true\n  upsert: true\n" } },
        { "a deployment env", new() { ["agents/refund-helper/agent.yaml"] = "" } }
    };

    [Theory]
    [MemberData(nameof(Tampered))]
    public async Task TamperedFiles_AreNotMerged_AndNothingIsApplied(string what, Dictionary<string, string?> files)
    {
        var number = await ProposeAsync(_builder, Draft());
        var pull = _git.Pulls.Single(p => p.Number == number);
        var original = _git.Repos["skanyxx/skanyxx-agents"][pull.Head]["agents/refund-helper/agent.yaml"];
        if (what == "a BYO image")
            files["agents/refund-helper/agent.yaml"] = original.Replace("  type: Declarative\n", "  type: BYO\n  byo:\n    deployment:\n      image: evil/agent:1\n");
        if (what == "a deployment env")
            files["agents/refund-helper/agent.yaml"] = original.Replace("    modelConfig: default-model-config\n    systemMessage",
                "    deployment:\n      env:\n      - name: X\n        value: y\n    modelConfig: default-model-config\n    systemMessage");
        _git.PushToBranch(pull.Head, files);

        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{number}", cancellationToken: TestContext.Current.CancellationToken);
        var merge = await MergeAsync(_owner, number);

        Assert.NotEmpty(detail.GetProperty("problems").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
        Assert.Equal("invalid", (await merge.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        Assert.True(pull.Open);
        Assert.Empty(StudioAgents());
        Assert.Empty(_kagent.ToolServers);
    }

    [Fact]
    public async Task APushAfterReview_IsNotMerged()
    {
        var number = await ProposeAsync(_builder, Draft());
        _git.MoveHeadOnMerge = true;

        var merge = await MergeAsync(_supervisor, number);

        Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
        Assert.Equal("stale", (await merge.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        Assert.Empty(StudioAgents());
    }

    [Fact]
    public async Task ATeamGrant_IsTheOwnersToMerge()
    {
        var number = await ProposeAsync(_builder, Draft(grants: [new { scope = "team:billing", search = true, upsert = false }]));

        var bySupervisor = await MergeAsync(_supervisor, number);
        var byOwner = await MergeAsync(_owner, number);

        Assert.Equal(HttpStatusCode.Forbidden, bySupervisor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
        var grants = await _owner.GetFromJsonAsync<JsonElement>("/api/memory/grants/refund-helper", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("team:billing", grants[0].GetProperty("scope").GetString());

        // Taking it away is the owner's too (D091).
        var removal = await ProposeAsync(_builder, Draft(grants: [new { scope = "company", search = true, upsert = false }]));
        Assert.Equal(HttpStatusCode.Forbidden, (await MergeAsync(_supervisor, removal)).StatusCode);
    }

    // --- Reconciler (D045) ---

    [Fact]
    public async Task TheReconciler_IsIdempotent_AndRepairsWhatKagentLost()
    {
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, await ProposeAsync(_builder, Draft()))).StatusCode);
        var secretBefore = Assert.Single(_kagent.Secrets).Key;
        Assert.Equal(0, await ReconcileAsync()); // the first pass adds the factory agent (setup made the owner after the loop's start)
        var writes = WriteCount();

        Assert.Equal(0, await ReconcileAsync());
        Assert.Equal(0, await ReconcileAsync());

        Assert.Equal(writes, WriteCount());
        Assert.Equal(secretBefore, Assert.Single(_kagent.Secrets).Key);

        // kagent lost the agent and its memory server: the next pass puts both back, with a new secret.
        _kagent.AgentObjects.TryRemove($"{Ns}/refund-helper", out _);
        _kagent.ToolServers.TryRemove($"{Ns}/skanyxx-memory-refund-helper", out _);
        _kagent.Secrets.Clear();
        Assert.Equal(0, await ReconcileAsync());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.NotEqual(secretBefore, Assert.Single(_kagent.Secrets).Key);
    }

    [Fact]
    public async Task AnInvalidFolderInMain_IsSkipped_NotApplied_AndTheRunningAgentStays()
    {
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, await ProposeAsync(_builder, Draft()))).StatusCode);
        _git.Main()["agents/refund-helper/agent.yaml"] = "kind: ConfigMap\n";
        _git.Main()["agents/evil/agent.yaml"] = "apiVersion: v1\nkind: Secret\n";
        _git.Main()["agents/evil/grants.yaml"] = "agent: evil\ngrants: []\n";

        await ReconcileAsync();

        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/evil"));
    }

    // --- Preview (D030, D032) ---

    [Fact]
    public async Task APreview_IsNotMerged_SearchesCompanyOnly_AndCannotUpsert()
    {
        var number = await ProposeAsync(_builder, Draft(grants:
        [
            new { scope = "company", search = true, upsert = true },
            new { scope = "team:billing", search = true, upsert = true }
        ]));

        var started = await _builder.PostAsync($"/api/studio/proposals/{number}/preview", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var name = $"preview-{number}-refund-helper";
        var preview = _kagent.AgentObjects[$"{Ns}/{name}"];
        var labels = preview["metadata"]!["labels"]!.AsObject();
        Assert.False(labels.ContainsKey("skanyxx.dev/merged"));
        Assert.Equal(number.ToString(), labels["skanyxx.dev/preview"]!.GetValue<string>());
        var tool = preview["spec"]!["declarative"]!["tools"]![0]!["mcpServer"]!;
        Assert.Equal(["memory_search"], tool["toolNames"]!.AsArray().Select(t => t!.GetValue<string>()));
        var grants = await _owner.GetFromJsonAsync<JsonElement>($"/api/memory/grants/{name}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("""[{"scope":"company","canSearch":true,"canUpsert":false}]""", grants.GetRawText());
        // Even called directly with its secret, the preview cannot write a card (D032).
        var header = _kagent.Secrets.Single(s => s.Value.Owner == $"{Ns}/skanyxx-memory-{name}").Value.Value;
        await using var mcp = await McpAsync(header["Bearer ".Length..], userId: null);
        var upsert = await mcp.CallToolAsync("memory_upsert", new Dictionary<string, object?>
        {
            ["key"] = "draft", ["type"] = "fact", ["what"] = "x", ["why"] = "y", ["version"] = 0, ["scope"] = "company"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(upsert.IsError);
        Assert.Contains("No upsert grant", Text(upsert));
        // Not in Chat, for anyone (D031), and Chat refuses to talk to it.
        Assert.DoesNotContain(name, await _employee.GetStringAsync("/api/chat/agents", TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound,
            (await _owner.PostAsJsonAsync("/api/chat", new { message = "hi", agentNamespace = Ns, agentName = name }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        // The builder and the supervisor talk to it in the studio.
        var turn = await _builder.PostAsJsonAsync($"/api/studio/proposals/{number}/chat", new { message = "refund window?" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
        Assert.Contains($"/api/a2a/{Ns}/{name}/", _kagent.Calls.Select(c => c.Path));
    }

    [Fact]
    public async Task APreview_IsRemoved_WhenItsProposalMerges_OrCloses()
    {
        var merged = await ProposeAsync(_builder, Draft());
        var closed = await ProposeAsync(_builder, Draft("faq-helper"));
        await _builder.PostAsync($"/api/studio/proposals/{merged}/preview", null, TestContext.Current.CancellationToken);
        await _builder.PostAsync($"/api/studio/proposals/{closed}/preview", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await _builder2.PostAsync($"/api/studio/proposals/{closed}/close", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _builder.PostAsync($"/api/studio/proposals/{closed}/close", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, merged)).StatusCode);

        Assert.Equal([$"{Ns}/refund-helper"], StudioAgents());
        Assert.Equal([$"{Ns}/skanyxx-memory-refund-helper"], _kagent.ToolServers.Keys);
        Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync($"/api/memory/grants/preview-{merged}-refund-helper", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("[]", (await _owner.GetStringAsync($"/api/memory/grants/preview-{merged}-refund-helper", TestContext.Current.CancellationToken)));
        Assert.False(_git.Pulls.Single(p => p.Number == closed).Merged);
    }

    // --- Factory (D5, D022) ---

    [Fact]
    public async Task TheFactory_DraftsTheForm_ForBuildersOnly_AndCreatesNothing()
    {
        _kagent.Answer = """Here you go: {"name":"refund-helper","description":"Refunds","instructions":"Answer refunds.","search":true,"upsert":false}""";

        var drafted = await _builder.PostAsJsonAsync("/api/studio/factory", new { request = "a refund helper using company memory" }, cancellationToken: TestContext.Current.CancellationToken);
        var supervisorOnly = await _supervisor.PostAsJsonAsync("/api/studio/factory", new { request = "x" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, drafted.StatusCode);
        var draft = await drafted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("refund-helper", draft.GetProperty("name").GetString());
        Assert.Equal("company", draft.GetProperty("grants")[0].GetProperty("scope").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, supervisorOnly.StatusCode);
        var factory = _kagent.AgentObjects[$"{Ns}/skanyxx-factory"];
        Assert.False(factory["metadata"]!["labels"]!.AsObject().ContainsKey("skanyxx.dev/merged"));
        Assert.Null(factory["spec"]!["declarative"]!["tools"]);
        Assert.Empty(_git.Pulls);
        Assert.DoesNotContain("skanyxx-factory", await _employee.GetStringAsync("/api/chat/agents", TestContext.Current.CancellationToken));
    }

    // --- Pages ---

    [Fact]
    public async Task TheStudioPage_IsForBuildersAndSupervisors_AndShowsTheYaml()
    {
        var number = await ProposeAsync(_builder, Draft());
        var builder = await SignInAsync("bo@skanyxx.example");
        var employee = await SignInAsync("eve@skanyxx.example");

        var page = await builder.GetAsync($"/Studio?pr={number}");
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"/Studio\"", html);
        Assert.Contains("kind: Agent", html);
        Assert.DoesNotContain("data-merge=", html); // a builder sees no merge button
        var refused = await employee.GetAsync("/Studio");
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Contains("/Login", refused.Headers.Location?.OriginalString);
        Assert.DoesNotContain("href=\"/Studio\"", await (await employee.GetAsync("/Library")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var supervisor = await SignInAsync("sue@skanyxx.example");
        Assert.Contains($"data-merge=\"{number}\"", await (await supervisor.GetAsync($"/Studio?pr={number}")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThePagesMergeForm_IsTheSameGate()
    {
        var number = await ProposeAsync(_builder, Draft());
        var builder = await SignInAsync("bo@skanyxx.example");
        var supervisor = await SignInAsync("sue@skanyxx.example");

        var refused = await builder.SubmitAsync($"/Studio?handler=Merge&pr={number}", [], tokenFrom: $"/Studio?pr={number}");
        var merged = await supervisor.SubmitAsync($"/Studio?handler=Merge&pr={number}", [], tokenFrom: $"/Studio?pr={number}");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, merged.StatusCode);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    // --- Legacy path and secrets hygiene ---

    [Fact]
    public async Task TheLegacyAgentsApi_NoLongerChangesKagent_ForAnyoneButTheOwner()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _builder.PostAsJsonAsync("/api/agents", new { name = "x" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.DeleteAsync("/api/agents/kagent__NS__seed", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PutAsJsonAsync("/api/agents/kagent__NS__seed/status", "Inactive", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TheGitToken_AndAgentSecrets_AreNeverReturnedOrLogged()
    {
        var number = await ProposeAsync(_builder, Draft());
        var bodies = string.Concat(
            await _supervisor.GetStringAsync("/api/studio", TestContext.Current.CancellationToken),
            await _supervisor.GetStringAsync($"/api/studio/proposals/{number}", TestContext.Current.CancellationToken),
            await (await MergeAsync(_supervisor, number)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var secret = Assert.Single(_kagent.Secrets).Value.Value["Bearer ".Length..];

        Assert.DoesNotContain(FakeGitea.Token, bodies);
        Assert.DoesNotContain(secret, bodies);
        Assert.DoesNotContain(FakeGitea.Token, _console.ToString());
        Assert.DoesNotContain(secret, _console.ToString());
        Assert.Contains("Studio proposal #1", _console.ToString()); // the audit lines are there
        Assert.All(_git.Calls, c => Assert.DoesNotContain(FakeGitea.Token, c.Path));
    }

    [Fact]
    public async Task WithoutGit_TheStudioSaysWhatToSet()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s => s["KAgent:Port"] = _kagent.Port.ToString());
        var owner = await host.OwnerAsync();

        var overview = await owner.GetFromJsonAsync<JsonElement>("/api/studio", cancellationToken: TestContext.Current.CancellationToken);
        var propose = await owner.PostAsJsonAsync("/api/studio/proposals", Draft(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Studio:Git:BaseUrl", overview.GetProperty("repoProblem").GetString());
        Assert.Equal(HttpStatusCode.Conflict, propose.StatusCode);
    }

    private static async Task WaitUntilAsync(Func<bool> done, string what, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"Waited {seconds} s for {what}.");
            await Task.Delay(50);
        }
    }

    private async Task<int> MergedAsync(string name, HttpClient? merger = null)
    {
        var number = await ProposeAsync(_builder, Draft(name));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(merger ?? _supervisor, number)).StatusCode);
        return number;
    }

    private async Task<JsonElement> PrincipalAsync(string agent) =>
        await _owner.GetFromJsonAsync<JsonElement>($"/api/memory/agents/{agent}/secret");


    // --- QA round 1 (D116–D122) ---

    /// <summary>M1: an untrusted file in main costs nothing: an alias bomb, deep nesting and an oversized file are each skipped, the running agent stays.</summary>
    [Theory]
    [InlineData("bomb")]
    [InlineData("deep")]
    [InlineData("oversize")]
    public async Task AHostileFileInMain_IsSkipped_AndThePassGoesOn(string kind)
    {
        await MergedAsync("refund-helper");
        await MergedAsync("faq-helper");
        _git.Main()["agents/refund-helper/agent.yaml"] = kind switch
        {
            "bomb" => "a: &a [x, x, x, x, x, x, x, x, x]\nb: &b [*a, *a, *a, *a, *a, *a, *a, *a, *a]\nc: [*b, *b, *b, *b, *b, *b, *b, *b, *b]\n",
            "deep" => new string('[', 10_000) + new string(']', 10_000),
            _ => "# " + new string('x', 70 * 1024) + "\n"
        };
        _kagent.AgentObjects.TryRemove($"{Ns}/faq-helper", out _);

        Assert.Equal(0, await ReconcileAsync());

        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper")); // skipped, not removed
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/faq-helper"));    // and the rest of the pass ran
        Assert.Contains("Studio skips", _console.ToString());
    }

    [Fact]
    public async Task AnOversizedFileInAProposal_IsAProblem_NotA500()
    {
        var number = await ProposeAsync(_builder, Draft());
        _git.PushToBranch(_git.Pulls.Single().Head, new() { ["agents/refund-helper/agent.yaml"] = new string('#', 70 * 1024) });

        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{number}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(detail.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("64 KB"));
        Assert.Equal(HttpStatusCode.Conflict, (await MergeAsync(_supervisor, number)).StatusCode);
    }

    /// <summary>M2 (D118): kagent failing between the issue and the new server leaves no lasting split; the next pass repairs it.</summary>
    [Fact]
    public async Task AFailureBetweenIssueAndServer_IsRepairedByTheNextPass()
    {
        var number = await ProposeAsync(_builder, Draft());
        _kagent.FailToolServerCreates = 1;

        var merged = await MergeAsync(_supervisor, number);

        Assert.Equal(HttpStatusCode.Accepted, merged.StatusCode);
        Assert.Contains("not live yet", await merged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_kagent.ToolServers.Keys, k => k.EndsWith("refund-helper"));
        Assert.True((await PrincipalAsync("refund-helper")).GetProperty("hasSecret").GetBoolean()); // issued, never deployed

        Assert.Equal(0, await ReconcileAsync());

        var (name, (_, header)) = Assert.Single(_kagent.Secrets, s => s.Value.Owner == $"{Ns}/skanyxx-memory-refund-helper");
        Assert.Matches("^skanyxx-memory-refund-helper-[a-f0-9]{12}$", name);
        await using var mcp = await McpAsync(header["Bearer ".Length..], userId: null);
        Assert.NotEqual(true, (await mcp.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "refund" }, cancellationToken: TestContext.Current.CancellationToken)).IsError);
        var writes = WriteCount();
        Assert.Equal(0, await ReconcileAsync());
        Assert.Equal(writes, WriteCount()); // and in place now: nothing issued again
    }

    /// <summary>M2: a secret memory holds that kagent never got (another replica's issue, a person's rotation) is detected and replaced.</summary>
    [Fact]
    public async Task ASecretKagentDoesNotHold_IsReplaced()
    {
        await MergedAsync("refund-helper");
        var before = Assert.Single(_kagent.Secrets).Key;
        (await _owner.PostAsJsonAsync("/api/memory/agents/refund-helper/secret", new { actsForUsers = false }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode(); // kagent's copy is dead now

        Assert.Equal(0, await ReconcileAsync());

        var after = Assert.Single(_kagent.Secrets);
        Assert.NotEqual(before, after.Key);
        Assert.Equal("studio", (await PrincipalAsync("refund-helper")).GetProperty("createdBy").GetString());
        await using (var mcp = await McpAsync(after.Value.Value["Bearer ".Length..], userId: null))
            Assert.NotEqual(true, (await mcp.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "x" }, cancellationToken: TestContext.Current.CancellationToken)).IsError);

        // Another replica issued a studio secret and never got to kagent: memory has one, kagent holds the old one.
        using (var scope = _host.Services.CreateScope())
            Assert.NotNull((await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>()
                .Send(new Skanyxx.Core.Platform.Memory.IssueStudioSecretCommand("other-replica", "refund-helper"), TestContext.Current.CancellationToken)).Value);

        Assert.Equal(0, await ReconcileAsync());

        var repaired = Assert.Single(_kagent.Secrets);
        Assert.NotEqual(after.Key, repaired.Key);
        await using var current = await McpAsync(repaired.Value.Value["Bearer ".Length..], userId: null);
        Assert.NotEqual(true, (await current.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "x" }, cancellationToken: TestContext.Current.CancellationToken)).IsError);
    }

    /// <summary>M3 (D120): one reconcile at a time across replicas — the periodic pass skips, a merge waits and then says so.</summary>
    [Fact]
    public async Task WhileAnotherReplicaReconciles_ThePassSkips_AndAMergeIsAccepted()
    {
        var number = await ProposeAsync(_builder, Draft());
        var locks = _host.Services.GetRequiredService<Skanyxx.Core.Platform.Memory.IStudioReconcileLock>();
        await using (var other = await locks.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None))
        {
            Assert.NotNull(other);
            _kagent.AgentObjects.TryRemove($"{Ns}/skanyxx-factory", out _);

            Assert.Equal(0, await ReconcileAsync());
            Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory")); // skipped: nothing written

            var merged = await MergeAsync(_supervisor, number);
            Assert.Equal(HttpStatusCode.Accepted, merged.StatusCode);
            Assert.True(_git.Pulls.Single().Merged);
            Assert.Empty(StudioAgents());
        }

        Assert.Equal(0, await ReconcileAsync());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    /// <summary>M4: a pass that throws something nobody expected is logged at Error, and the Host and the loop go on.</summary>
    [Fact]
    public async Task AThrowingPass_DoesNotStopTheHost()
    {
        await using var git = await FakeGitea.StartAsync();
        git.TreeBody = "[]"; // a tree that is not an object: the pass throws InvalidOperationException
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:ReconcileSeconds"] = "1";
        });
        (await host.BootstrapAsync()).EnsureSuccessStatusCode();

        await WaitUntilAsync(() => CountOf(_console.ToString(), "Studio reconcile failed") >= 3, "three failed passes");

        var loop = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().Single(h => h.GetType().Name == "StudioReconcileLoop");
        Assert.False(loop.ExecuteTask!.IsCompleted);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        Assert.Equal(HttpStatusCode.OK, (await host.Client().GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>M5 (D117): a memory principal someone made by hand is never taken over — refused at propose and at merge.</summary>
    [Fact]
    public async Task ANameMemoryHoldsForSomeoneElse_IsRefused_AtProposeAndAtMerge()
    {
        (await _owner.PostAsJsonAsync("/api/memory/agents/foo-bot/secret", new { actsForUsers = false }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await _owner.PutAsJsonAsync("/api/memory/grants/foo-bot", new { grants = new[] { new { scope = "team:billing", canSearch = true, canUpsert = false } } }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var propose = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft("foo-bot"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, propose.StatusCode);
        Assert.Equal("taken", (await propose.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());

        // The same files pushed and opened by hand: refused at review and merge too.
        var (agentYaml, grantsYaml) = Render(Draft("foo-bot"));
        _git.PushToBranch("by-hand", new() { ["agents/foo-bot/agent.yaml"] = agentYaml, ["agents/foo-bot/grants.yaml"] = grantsYaml });
        var number = _git.OpenPull("by-hand", "New agent foo-bot", "Agent: foo-bot\nProposed-by: someone\n");
        var detail = await _owner.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{number}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(detail.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("did not create"));
        Assert.Equal(HttpStatusCode.Conflict, (await MergeAsync(_owner, number)).StatusCode);
        var grants = await _owner.GetFromJsonAsync<JsonElement>("/api/memory/grants/foo-bot", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("team:billing", grants[0].GetProperty("scope").GetString());
        Assert.NotEqual("studio", (await PrincipalAsync("foo-bot")).GetProperty("createdBy").GetString());
    }

    /// <summary>M6/G2 (D119): a lost git volume never re-creates the repo, and never takes the live agents down.</summary>
    [Fact]
    public async Task AGoneRepo_IsNotRecreated_AndRemovesNothing()
    {
        await MergedAsync("refund-helper");
        _git.WipeRepo();

        Assert.Equal("StudioRepoException", (await Assert.ThrowsAnyAsync<Exception>(ReconcileTargetAsync)).GetType().Name);

        Assert.False(_git.Repos.ContainsKey("skanyxx/skanyxx-agents"));
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.True((await PrincipalAsync("refund-helper")).GetProperty("hasSecret").GetBoolean());
        Assert.Contains("is gone", (await _supervisor.GetFromJsonAsync<JsonElement>("/api/studio", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("repoProblem").GetString());
        var propose = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft("other"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, propose.StatusCode);
        Assert.Equal("repo", (await propose.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ADifferentRepoUnderTheSameName_RemovesNothing_UntilTheOwnerConfirms()
    {
        await MergedAsync("refund-helper");
        _git.RecreateRepo(); // empty main, new git id

        Assert.Equal("StudioRepoException", (await Assert.ThrowsAnyAsync<Exception>(ReconcileTargetAsync)).GetType().Name);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.Contains("not the one the studio recorded", (await _supervisor.GetFromJsonAsync<JsonElement>("/api/studio", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("repoProblem").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken)).StatusCode);
        var confirmed = await _owner.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper")); // the owner chose this repo, and it is empty
        Assert.False((await PrincipalAsync("refund-helper")).GetProperty("hasSecret").GetBoolean());
    }

    [Fact]
    public async Task AMainWithoutAgents_OrAMassRemoval_IsBraked()
    {
        foreach (var name in new[] { "agent-a", "agent-b", "agent-c", "agent-d" })
            await MergedAsync(name);
        var main = _git.Main();
        var saved = main.Where(f => f.Key.StartsWith("agents/")).ToDictionary();
        foreach (var path in saved.Keys)
            main.Remove(path);

        Assert.Equal(4, await ReconcileAsync()); // main has no agents/: nothing removed
        Assert.Equal(4, StudioAgents().Count);
        Assert.Contains("has no agents/ folder", _console.ToString());

        foreach (var (path, content) in saved.Where(f => f.Key.StartsWith("agents/agent-d/")))
            main[path] = content;
        Assert.Equal(3, await ReconcileAsync()); // three of four at once: braked
        Assert.Equal(4, StudioAgents().Count);

        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal([$"{Ns}/agent-d"], StudioAgents());

        await MergedAsync("agent-e");
        await MergedAsync("agent-f");
        main = _git.Main(); // a merge replaces main
        foreach (var path in main.Keys.Where(k => k.StartsWith("agents/agent-e/")).ToList())
            main.Remove(path);
        Assert.Equal(0, await ReconcileAsync()); // one of three: an ordinary removal
        Assert.Equal([$"{Ns}/agent-d", $"{Ns}/agent-f"], StudioAgents().Order());
    }

    /// <summary>m4: main that lost its protection (or a public repo) is not trusted: nothing is applied.</summary>
    [Fact]
    public async Task AnUnprotectedMain_OrAPublicRepo_StopsThePass()
    {
        await MergedAsync("refund-helper");
        _git.Protections[0]["enable_push"] = true;
        _kagent.AgentObjects.TryRemove($"{Ns}/refund-helper", out _);

        Assert.Equal("StudioRepoException", (await Assert.ThrowsAnyAsync<Exception>(ReconcileTargetAsync)).GetType().Name);
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));

        _git.Protections[0]["enable_push"] = false;
        _git.PublicRepo = true;
        var overview = await _supervisor.GetFromJsonAsync<JsonElement>("/api/studio", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("not private", overview.GetProperty("repoProblem").GetString());
    }

    /// <summary>M7 (D122): skills only by digest from the owner's registries, and never in a preview.</summary>
    [Fact]
    public async Task Skills_NeedADigest_AndPreviewsRunWithout()
    {
        const string skill = "ghcr.io/acme/skills/refunds@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var tagged = await _builder.PostAsJsonAsync("/api/studio/proposals", WithSkills(Draft(), "ghcr.io/acme/skills/refunds:1"), cancellationToken: TestContext.Current.CancellationToken);
        var foreign = await _builder.PostAsJsonAsync("/api/studio/proposals", WithSkills(Draft(), "docker.io/evil/skill@sha256:" + new string('0', 64)), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, tagged.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var number = await ProposeAsync(_builder, WithSkills(Draft(), skill));
        Assert.Equal(HttpStatusCode.OK, (await _builder.PostAsync($"/api/studio/proposals/{number}/preview", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Null(_kagent.AgentObjects[$"{Ns}/preview-{number}-refund-helper"]["spec"]!["skills"]);

        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, number)).StatusCode);
        Assert.Equal(skill, _kagent.AgentObjects[$"{Ns}/refund-helper"]["spec"]!["skills"]!["refs"]![0]!.GetValue<string>());
    }

    /// <summary>G1 + D116: what changes kagent, the cluster or runs a process is the owner's, like D109.</summary>
    [Fact]
    public async Task LegacyRoutesThatChangeKagent_AreTheOwnersOnly()
    {
        foreach (var who in new[] { _supervisor, _builder, _employee })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsJsonAsync("/api/toolservers", new { name = "x" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.DeleteAsync($"/api/toolservers/{Ns}/skanyxx-memory-refund-helper", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsJsonAsync("/api/toolservers/x/tools/y/invoke", new { }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsJsonAsync("/api/cloudtools/repl", new { command = "1" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.GetAsync("/api/cloudtools/search?query=x", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsJsonAsync("/api/cloud/scale-up", new { name = "x", @namespace = "y", replicas = 0 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.PostAsJsonAsync("/api/hooks", new { }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await who.DeleteAsync("/api/sessions/any", TestContext.Current.CancellationToken)).StatusCode);
        }
        Assert.NotEqual(HttpStatusCode.Forbidden, (await _owner.PostAsJsonAsync("/api/cloudtools/repl", new { command = "" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>m1: once git merged, the person closing the page cancels nothing: the agent still goes live.</summary>
    [Fact]
    public async Task AClientDisconnectAfterTheMerge_StillApplies()
    {
        var number = await ProposeAsync(_builder, Draft());
        using var cts = new CancellationTokenSource();
        _git.OnMerge = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, cts.Token));

        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"), "the apply after a disconnect");
    }

    /// <summary>m2: git merged but its answer was lost: the merge is reported as what it is.</summary>
    [Fact]
    public async Task AMergeWhoseAnswerIsLost_IsStillAMerge()
    {
        var number = await ProposeAsync(_builder, Draft());
        _git.LoseMergeAnswer = true;

        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, number)).StatusCode);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    /// <summary>m3: past git's page size nothing is lost: open proposals beyond the 50th, and main beyond 1,000 files.</summary>
    [Fact]
    public async Task Paging_SeesEveryOpenProposal_AndEveryFileInMain()
    {
        for (var i = 0; i < 55; i++)
        {
            _git.PushToBranch($"other-{i}", new() { [$"notes/{i}.md"] = "x" });
            _git.OpenPull($"other-{i}", $"Note {i}", $"Note {i}");
        }
        var number = await ProposeAsync(_builder, Draft());
        Assert.True(number > 50);
        Assert.Equal(HttpStatusCode.OK, (await _builder.PostAsync($"/api/studio/proposals/{number}/preview", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("pending", (await (await _builder2.PostAsJsonAsync("/api/studio/proposals", Draft(), cancellationToken: TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());

        Assert.Equal(0, await ReconcileAsync());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/preview-{number}-refund-helper"));

        await MergedAsync("faq-helper");
        for (var i = 0; i < 1_200; i++)
            _git.Main()[$"a/{i:0000}.md"] = "x"; // sorts before agents/: the agent is on the second page
        Assert.Equal(0, await ReconcileAsync());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/faq-helper"));
    }

    /// <summary>m5 (D121): the owner's emergency stop, honoured by every pass until resumed.</summary>
    [Fact]
    public async Task TheOwnersStop_TakesAnAgentDown_AndKeepsItDown_UntilResumed()
    {
        await MergedAsync("refund-helper");

        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsync("/api/studio/agents/refund-helper/suspend", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/agents/refund-helper/suspend", null, TestContext.Current.CancellationToken)).StatusCode);

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.DoesNotContain(_kagent.ToolServers.Keys, k => k.EndsWith("-refund-helper"));
        Assert.False((await PrincipalAsync("refund-helper")).GetProperty("hasSecret").GetBoolean());
        Assert.Equal(0, await ReconcileAsync());
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.Equal("[]", await _owner.GetStringAsync("/api/memory/grants/refund-helper", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/agents/refund-helper/resume", null, TestContext.Current.CancellationToken)).StatusCode);
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"), "the resumed agent");
        await WaitUntilAsync(() => _kagent.ToolServers.ContainsKey($"{Ns}/skanyxx-memory-refund-helper"), "its memory server");
    }

    /// <summary>m6: who must merge is decided from memory's grants too, not only main's file.</summary>
    [Fact]
    public async Task ATeamGrantMemoryHolds_NeedsTheOwner_EvenWhenMainsFileIsBroken()
    {
        var number = await ProposeAsync(_builder, Draft(grants: [new { scope = "team:billing", search = true, upsert = false }]));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_owner, number)).StatusCode);
        _git.Main()["agents/refund-helper/grants.yaml"] = "broken: [";

        var removal = await ProposeAsync(_builder, Draft());

        Assert.Equal(HttpStatusCode.Forbidden, (await MergeAsync(_supervisor, removal)).StatusCode);
    }

    /// <summary>m7: only a pull request into main is a proposal.</summary>
    [Fact]
    public async Task APullIntoAnotherBranch_IsNotAProposal()
    {
        var (agentYaml, grantsYaml) = Render(Draft());
        _git.Repos["skanyxx/skanyxx-agents"]["side"] = new(_git.Main());
        _git.PushToBranch("into-side", new() { ["agents/refund-helper/agent.yaml"] = agentYaml, ["agents/refund-helper/grants.yaml"] = grantsYaml });
        var number = _git.OpenPull("into-side", "New agent refund-helper", "Agent: refund-helper\n", @base: "side");

        var merge = await MergeAsync(_supervisor, number);

        Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
        Assert.Contains("this one targets 'side'", await merge.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(StudioAgents());
    }

    /// <summary>m8: a preview that fails half way leaves no grant, secret or memory server behind.</summary>
    [Fact]
    public async Task APreviewThatFailsHalfWay_LeavesNothing()
    {
        var number = await ProposeAsync(_builder, Draft());
        _kagent.FailToolServerCreates = 1;

        Assert.NotEqual(HttpStatusCode.OK, (await _builder.PostAsync($"/api/studio/proposals/{number}/preview", null, TestContext.Current.CancellationToken)).StatusCode);

        var name = $"preview-{number}-refund-helper";
        Assert.Equal("[]", await _owner.GetStringAsync($"/api/memory/grants/{name}", TestContext.Current.CancellationToken));
        Assert.False((await PrincipalAsync(name)).GetProperty("hasSecret").GetBoolean());
        Assert.DoesNotContain($"{Ns}/{name}", _kagent.AgentObjects.Keys);
    }

    [Fact]
    public async Task ANameStartingWithADigit_IsRefused() =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _builder.PostAsJsonAsync("/api/studio/proposals", Draft("1refund"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

    /// <summary>m11: a builder has a bounded number of open proposals (each can run a preview pod).</summary>
    [Fact]
    public async Task ABuildersOpenProposals_AreCapped()
    {
        for (var i = 0; i < 10; i++)
        {
            _git.PushToBranch($"b-{i}", new() { [$"notes/{i}.md"] = "x" });
            _git.OpenPull($"b-{i}", $"New agent a{i}", $"Agent: a{i}\nProposed-by: {_builderId}\n");
        }

        var refused = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("too-many", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Created, (await _builder2.PostAsJsonAsync("/api/studio/proposals", Draft(), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>m14: kagent listing a ref twice (or none) is not a 500.</summary>
    [Fact]
    public async Task DuplicateToolServerRefs_DoNotBreakTheForm()
    {
        _kagent.DuplicateToolServerRefs = true;

        Assert.Equal(HttpStatusCode.OK, (await _builder.GetAsync("/api/studio/options", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>G8: offboarding the supervisor who merged an agent does not cut the agent off (its secret is the studio's).</summary>
    [Fact]
    public async Task OffboardingTheMerger_LeavesTheAgentWorking()
    {
        await MergedAsync("refund-helper");
        (await _owner.PutAsJsonAsync($"/api/identity/people/{_supervisorId}/roles", new { roles = new[] { "employee" } }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        Assert.True((await PrincipalAsync("refund-helper")).GetProperty("hasSecret").GetBoolean());
        var header = Assert.Single(_kagent.Secrets).Value.Value;
        await using var mcp = await McpAsync(header["Bearer ".Length..], userId: null);
        Assert.NotEqual(true, (await mcp.CallToolAsync("memory_search", new Dictionary<string, object?> { ["query"] = "x" }, cancellationToken: TestContext.Current.CancellationToken)).IsError);
        Assert.Equal(HttpStatusCode.OK, (await _employee.PostAsJsonAsync("/api/chat", new { message = "hi", agentNamespace = Ns, agentName = "refund-helper" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    // --- QA round 2 ---

    /// <summary>
    /// Round 2 m1, m4, m6 (D119): a protection rule that went missing on the recorded repo is refused, never quietly
    /// re-created; overview, propose, preview and merge only read git and answer reason "repo"; the owner's confirm
    /// re-protects.
    /// </summary>
    [Fact]
    public async Task ADeletedProtectionRule_IsRefused_NotRecreated_UntilTheOwnerConfirms()
    {
        await MergedAsync("refund-helper");
        var pending = await ProposeAsync(_builder, Draft("other-helper"));
        _git.Protections.Clear();
        _kagent.AgentObjects.TryRemove($"{Ns}/refund-helper", out _);
        int ProtectionWrites() => _git.Calls.Count(c => c.Method == "POST" && c.Path.Contains("/branch_protections"));
        var writes = ProtectionWrites();

        Assert.Equal("StudioRepoException", (await Assert.ThrowsAnyAsync<Exception>(ReconcileTargetAsync)).GetType().Name);
        Assert.Contains("Studio refuses the agent repo", _console.ToString());
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
        Assert.Contains("not protected", (await _supervisor.GetFromJsonAsync<JsonElement>("/api/studio", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("repoProblem").GetString());
        foreach (var refused in new[]
                 {
                     await _builder.PostAsJsonAsync("/api/studio/proposals", Draft("third-helper"), cancellationToken: TestContext.Current.CancellationToken),
                     await _builder.PostAsync($"/api/studio/proposals/{pending}/preview", null, TestContext.Current.CancellationToken),
                     await MergeAsync(_supervisor, pending)
                 })
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("repo", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        }
        Assert.Empty(_git.Protections);
        Assert.Equal(writes, ProtectionWrites());
        Assert.DoesNotContain($"{Ns}/preview-{pending}-other-helper", _kagent.AgentObjects.Keys);

        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Single(_git.Protections);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    /// <summary>Round 2 m3: merging a change to a suspended agent says so, and the agent stays down.</summary>
    [Fact]
    public async Task MergingASuspendedAgent_SaysItIsSuspended_NotLive()
    {
        await MergedAsync("refund-helper");
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/agents/refund-helper/suspend", null, TestContext.Current.CancellationToken)).StatusCode);
        var change = await ProposeAsync(_builder, Draft(ttl: 7)); // proposed while suspended

        var merge = await MergeAsync(_supervisor, change);

        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
        var message = (await merge.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("message").GetString()!;
        Assert.Contains("suspended", message);
        Assert.DoesNotContain("is live", message);
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    /// <summary>Round 2 I3 (D121): suspending an agent takes down the previews of its open proposals, and starts none.</summary>
    [Fact]
    public async Task SuspendingAnAgent_TakesItsPreviewsDown()
    {
        await MergedAsync("refund-helper");
        var change = await ProposeAsync(_builder, Draft(ttl: 7));
        Assert.Equal(HttpStatusCode.OK, (await _builder.PostAsync($"/api/studio/proposals/{change}/preview", null, TestContext.Current.CancellationToken)).StatusCode);
        var preview = $"preview-{change}-refund-helper";
        Assert.Contains($"{Ns}/{preview}", _kagent.AgentObjects.Keys);

        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/agents/refund-helper/suspend", null, TestContext.Current.CancellationToken)).StatusCode);

        Assert.DoesNotContain($"{Ns}/{preview}", _kagent.AgentObjects.Keys);
        Assert.DoesNotContain($"{Ns}/skanyxx-memory-{preview}", _kagent.ToolServers.Keys);
        Assert.False((await PrincipalAsync(preview)).GetProperty("hasSecret").GetBoolean());
        var again = await _builder.PostAsync($"/api/studio/proposals/{change}/preview", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("suspended", await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain($"{Ns}/{preview}", _kagent.AgentObjects.Keys);
    }

    /// <summary>
    /// Round 2 m5: a lone preview memory server is swept only with a numbered name and memory's record that the studio
    /// claimed it; a hand-made server under the same prefix stays.
    /// </summary>
    [Fact]
    public async Task TheOrphanSweep_TakesOnlyTheStudiosPreviewServers()
    {
        var number = await ProposeAsync(_builder, Draft());
        Assert.Equal(HttpStatusCode.OK, (await _builder.PostAsync($"/api/studio/proposals/{number}/preview", null, TestContext.Current.CancellationToken)).StatusCode);
        var preview = $"preview-{number}-refund-helper";
        _kagent.AgentObjects.TryRemove($"{Ns}/{preview}", out _); // only its memory server is left
        _git.Pulls[number - 1].Open = false;
        _kagent.ToolServers[$"{Ns}/skanyxx-memory-preview-notes"] = new JsonObject();
        _kagent.ToolServers[$"{Ns}/skanyxx-memory-preview-99-notes"] = new JsonObject();

        Assert.Equal(0, await ReconcileAsync());

        Assert.DoesNotContain($"{Ns}/skanyxx-memory-{preview}", _kagent.ToolServers.Keys);
        Assert.Contains($"{Ns}/skanyxx-memory-preview-notes", _kagent.ToolServers.Keys);
        Assert.Contains($"{Ns}/skanyxx-memory-preview-99-notes", _kagent.ToolServers.Keys);
    }

    private static object WithSkills(object draft, string skill)
    {
        var json = JsonSerializer.SerializeToNode(draft)!.AsObject();
        json["skills"] = new JsonArray(skill);
        return json;
    }

    private static (string Agent, string Grants) Render(object draft) =>
        Skanyxx.Module.Agents.Studio.Definition.AgentManifest.Render(
            JsonSerializer.Deserialize<Skanyxx.Core.Platform.Studio.AgentDraft>(JsonSerializer.Serialize(draft), JsonSerializerOptions.Web)!, Ns);

    private Task ReconcileTargetAsync() => ReconcileAsync();

    private static int CountOf(string text, string what) => (text.Length - text.Replace(what, "").Length) / what.Length;

    /// <summary>Agents in kagent other than the factory (which the reconciler keeps from the start).</summary>
    private List<string> StudioAgents() => [.. _kagent.AgentObjects.Keys.Where(k => k != $"{Ns}/skanyxx-factory")];

    private int WriteCount() =>
        _kagent.Calls.Count(c => c.Method is "POST" or "PUT" or "DELETE" && (c.Path.StartsWith("/api/agents") || c.Path.StartsWith("/api/toolservers")));

    private async Task<McpClient> McpAsync(string secret, string? userId)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {secret}" };
        if (userId is not null)
            headers["X-User-Id"] = userId;
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_host.BaseAddress, "/mcp/memory"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = headers
        }, LoggerFactory.Create(_ => { }));
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text)) +
        (result.StructuredContent is { } s ? JsonSerializer.Serialize(s) : "");

    private static void Created(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    private async Task<Browser> SignInAsync(string email)
    {
        var browser = new Browser(_host);
        var password = email == HostApp.OwnerEmail ? HostApp.OwnerPassword : Password;
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password })).StatusCode);
        return browser;
    }

    private async Task<(HttpClient Client, string Id)> PersonAsync(string email, string role)
    {
        var invite = await _owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        return (_host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!), body.GetProperty("user").GetProperty("id").GetString()!);
    }
}
