using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Studio for non-coders, end to end (D360–D368), on the real host against <see cref="FakeGitea"/>, <see cref="FakeKAgent"/>
/// and <see cref="FakeArgo"/>: template → form → pull request → CI checks → merge (by role) → deployed to kagent, the cloud
/// tools licence, CI's own validation endpoint and token, and that no token reaches a log line or an audit row.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class StudioDeliveryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";
    private const string Ns = "kagent";
    private const string CiToken = "ci-token-NEVER-SHOWN-0123456789abcdef";

    private readonly AuditProbe _audit = new();
    private readonly List<IAsyncDisposable> _owned = [];
    private FakeGitea _git = null!;
    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!;
    private HttpClient _supervisor = null!;
    private HttpClient _builder = null!;
    private HttpClient _employee = null!;
    private StringWriter _console = null!;
    private TextWriter _originalOut = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _originalOut = Console.Out;
        _console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(_console));
        _kagent = await FakeKAgent.StartAsync();
        _kagent.ToolServers[$"{Ns}/aws-cost"] = new JsonObject();
        _kagent.DiscoveredTools[$"{Ns}/aws-cost"] = ["get_costs"];
        await StartAsync(_ => { });
    }

    /// <summary>A host on a fresh database and git with the studio's delivery settings, plus <paramref name="configure"/>.</summary>
    private async Task StartAsync(Action<Dictionary<string, string>> configure)
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            await _git.DisposeAsync();
        }
        _git = await FakeGitea.StartAsync();
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = _git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:MemoryMcpUrl"] = "http://skanyxx.skanyxx.svc:8080/mcp/memory";
            s["Studio:McpServers:0"] = "jira";
            s["Studio:McpServers:1"] = "aws-cost";
            s["Studio:CloudToolServers:0"] = "aws-cost";
            s["Studio:Ci:Token"] = CiToken;
            s["Studio:ReconcileSeconds"] = "3600";
            s["Studio:LockWaitSeconds"] = "2";
            configure(s);
        }, services =>
        {
            services.AddSingleton(_audit);
            services.AddTransient<IRequestHandler<RecordAuditCommand>, ProbeAuditHandler>();
        });
        _owner = await _host.OwnerAsync();
        await WaitUntilAsync(() => _git.Protections.Count == 1, "the first reconcile pass after setup");
        _supervisor = await PersonAsync("sue@skanyxx.example", "supervisor");
        _builder = await PersonAsync("bo@skanyxx.example", "builder");
        _employee = await PersonAsync("eve@skanyxx.example", "employee");
    }

    public async ValueTask DisposeAsync()
    {
        Console.SetOut(_originalOut);
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
        await _git.DisposeAsync();
        foreach (var owned in _owned)
            await owned.DisposeAsync();
    }

    // --- Templates and the guided form (D360, D361) ---

    [Fact]
    public async Task Templates_AreForStudioPeople_OnTheOwnersModel_AndTheCloudOneIsLockedWithoutTheLicence()
    {
        var templates = await _builder.GetFromJsonAsync<JsonElement>("/api/studio/templates", Ct);
        Assert.Equal(["support", "research", "ops", "data", "cloud-ops"], templates.EnumerateArray().Select(t => t.GetProperty("id").GetString()));
        Assert.All(templates.EnumerateArray(), t => Assert.Equal("default-model-config", t.GetProperty("draft").GetProperty("modelConfig").GetString()));
        var cloud = templates.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "cloud-ops");
        Assert.True(cloud.GetProperty("locked").GetBoolean());
        Assert.Equal(0, cloud.GetProperty("draft").GetProperty("mcpTools").GetArrayLength());
        Assert.False(templates.EnumerateArray().First().GetProperty("locked").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.GetAsync("/api/studio/templates", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().GetAsync("/api/studio/templates", Ct)).StatusCode);
    }

    [Fact]
    public async Task ATemplate_BecomesAPullRequest_WithTheManifestItRenders_AndNothingLive()
    {
        var draft = await TemplateDraftAsync(_builder, "support", "helpdesk");
        var preview = await (await _builder.PostAsJsonAsync("/api/studio/manifest", draft, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(0, preview.GetProperty("problems").GetArrayLength());
        var yaml = preview.GetProperty("agentYaml").GetString()!;
        Assert.Contains("name: helpdesk", yaml);
        Assert.Contains("You help customers", yaml);

        var number = await ProposeAsync(_builder, draft);

        var pull = _git.Pulls.Single(p => p.Number == number);
        Assert.StartsWith("Agent: helpdesk\n", pull.Body);
        var branch = _git.Repos["skanyxx/skanyxx-agents"][pull.Head];
        Assert.Equal(yaml, branch["agents/helpdesk/agent.yaml"]);
        Assert.Equal(preview.GetProperty("grantsYaml").GetString(), branch["agents/helpdesk/grants.yaml"]);
        Assert.DoesNotContain("agents/helpdesk/agent.yaml", _git.Main().Keys);
        Assert.False(_kagent.AgentObjects.ContainsKey($"{Ns}/helpdesk"));
    }

    [Fact]
    public async Task TheYamlPreview_SaysWhatIsWrong_InPlainWords_AndChangesNothing()
    {
        var calls = _git.Calls.Count(c => c.Method != "GET");
        var preview = await (await _builder.PostAsJsonAsync("/api/studio/manifest", new
        {
            name = "Bad Name",
            description = "",
            modelConfig = "default-model-config",
            instructions = "",
            skills = Array.Empty<string>(),
            mcpTools = new[] { new { server = "aws-cost", tools = new[] { "get_costs" } } },
            grants = Array.Empty<object>(),
            memoryTtlDays = (int?)null
        }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        var problems = preview.GetProperty("problems").EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(problems, p => p.StartsWith("Name: lowercase letters"));
        Assert.Contains(problems, p => p.StartsWith("Instructions: required"));
        Assert.Contains(problems, p => p.Contains("cloud tools are a paid feature"));
        Assert.Equal(calls, _git.Calls.Count(c => c.Method != "GET"));
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/studio/manifest", new { name = "x" }, Ct)).StatusCode);
    }

    // --- Cloud tools are paid (D364) ---

    [Fact]
    public async Task CloudTools_AreLocked_UntilTheLicenceIsOn()
    {
        var options = await _builder.GetFromJsonAsync<JsonElement>("/api/studio/options", Ct);
        Assert.Equal(["aws-cost"], options.GetProperty("paidServers").EnumerateArray().Select(s => s.GetString()));
        Assert.False(options.GetProperty("cloudToolsLicensed").GetBoolean());

        var refused = await _builder.PostAsJsonAsync("/api/studio/proposals", Draft("costs", tools: [new { server = "aws-cost", tools = new[] { "get_costs" } }]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("paid feature", await refused.Content.ReadAsStringAsync(Ct));
        Assert.Empty(_git.Pulls);

        // A proposal pushed in git with the cloud tool cannot be merged either.
        var free = await ProposeAsync(_builder, Draft("costs2"));
        var paid = new AgentDraft("costs2", "Answers refund questions", "default-model-config", "Answer refund questions from company memory.", [],
            [new McpToolChoice("aws-cost", ["get_costs"])], [new StudioGrant("company", true, false)], null);
        var (agentYaml, grantsYaml) = AgentManifest.Render(paid, Ns);
        _git.PushToBranch(_git.Pulls.Single(p => p.Number == free).Head, new() { ["agents/costs2/agent.yaml"] = agentYaml, ["agents/costs2/grants.yaml"] = grantsYaml });
        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{free}", Ct);
        Assert.Contains(detail.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("paid feature"));
        var merge = await _supervisor.PostAsync($"/api/studio/proposals/{free}/merge", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, merge.StatusCode);
        Assert.Contains("paid feature", await merge.Content.ReadAsStringAsync(Ct));

        // With the licence, the cloud template is usable and brings the cloud server's discovered tools.
        await StartAsync(s => s["Skanyxx:Licence:CloudTools"] = "true");
        var templates = await _builder.GetFromJsonAsync<JsonElement>("/api/studio/templates", Ct);
        var cloud = templates.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "cloud-ops");
        Assert.False(cloud.GetProperty("locked").GetBoolean());
        var tools = cloud.GetProperty("draft").GetProperty("mcpTools").EnumerateArray().Single();
        Assert.Equal("aws-cost", tools.GetProperty("server").GetString());
        Assert.Equal(["get_costs"], tools.GetProperty("tools").EnumerateArray().Select(t => t.GetString()));
        Assert.True((await _builder.GetFromJsonAsync<JsonElement>("/api/studio/options", Ct)).GetProperty("cloudToolsLicensed").GetBoolean());
        await ProposeAsync(_builder, await TemplateDraftAsync(_builder, "cloud-ops", "cloud-costs"));
    }

    // --- Checks (D362) and the timeline (D365) ---

    [Fact]
    public async Task Checks_FromGit_ShowOnTheChange_WithTheirLinks_AndTheTimeline()
    {
        var number = await ProposeAsync(_builder, Draft("checked"));
        var head = _git.HeadOf(number);

        var none = await ChangeAsync(_builder, number);
        Assert.Equal("None", none.GetProperty("checks").GetProperty("state").GetString());
        Assert.Equal($"{_git.BaseUrl}/skanyxx/skanyxx-agents/pulls/{number}", none.GetProperty("url").GetString());

        _git.Report(head, "validate-agents / validate", "pending");
        Assert.Equal("Pending", (await ChangeAsync(_builder, number)).GetProperty("checks").GetProperty("state").GetString());

        _git.Report(head, "validate-agents / validate", "failure", "http://git.example/actions/runs/7");
        var failed = await ChangeAsync(_builder, number);
        Assert.Equal("Failed", failed.GetProperty("checks").GetProperty("state").GetString());
        var run = failed.GetProperty("checks").GetProperty("runs").EnumerateArray().Single();
        Assert.Equal("http://git.example/actions/runs/7", run.GetProperty("url").GetString());
        var steps = failed.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["Draft", "Pull request opened", "Checks", "Merged", "Deployed"], steps.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal("Failed", steps[2].GetProperty("state").GetString());

        _git.Report(head, "validate-agents / validate", "success");
        var detail = await _supervisor.GetFromJsonAsync<JsonElement>($"/api/studio/proposals/{number}", Ct);
        Assert.Equal("Passed", detail.GetProperty("checks").GetProperty("state").GetString());
        Assert.EndsWith($"/pulls/{number}", detail.GetProperty("url").GetString());
        Assert.Equal("Done", detail.GetProperty("steps")[2].GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct)).StatusCode);
        var merged = await ChangeAsync(_builder, number);
        Assert.Equal("Merged", merged.GetProperty("status").GetString());
        Assert.Equal("Healthy", merged.GetProperty("deploy").GetProperty("state").GetString());
        Assert.Equal("reconciler", merged.GetProperty("deploy").GetProperty("source").GetString());
        Assert.All(merged.GetProperty("steps").EnumerateArray(), s => Assert.Equal("Done", s.GetProperty("state").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.GetAsync("/api/studio/changes", Ct)).StatusCode);
    }

    [Fact]
    public async Task WithRequiredChecks_AMergeWaitsForPassingChecks()
    {
        await StartAsync(s => s["Studio:RequireChecks"] = "true");
        var number = await ProposeAsync(_builder, Draft("gated"));

        var none = await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
        Assert.Contains("No CI has reported", await none.Content.ReadAsStringAsync(Ct));
        _git.Report(_git.HeadOf(number), "validate", "failure");
        var failed = await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Contains("checks failed", await failed.Content.ReadAsStringAsync(Ct));
        Assert.False(_git.Pulls.Single(p => p.Number == number).Merged);

        _git.Report(_git.HeadOf(number), "validate", "success");
        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct)).StatusCode);

        // A direct apply stops at the open pull request until its checks pass.
        var applied = await _supervisor.PostAsJsonAsync("/api/studio/apply", Draft("gated-direct"), Ct);
        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        Assert.Contains("once its checks pass", await applied.Content.ReadAsStringAsync(Ct));
        Assert.True(_git.Pulls.Last().Open);
    }

    // --- Direct by role, PR for users (D366) ---

    [Fact]
    public async Task ASupervisor_AppliesDirectly_ThroughOneMergedPullRequest_AndItIsAudited()
    {
        var applied = await _supervisor.PostAsJsonAsync("/api/studio/apply", Draft("direct"), Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Contains("Applied directly", await applied.Content.ReadAsStringAsync(Ct));
        var pull = Assert.Single(_git.Pulls);
        Assert.True(pull.Merged);
        Assert.Contains("agents/direct/agent.yaml", _git.Main().Keys);
        Assert.True(_kagent.AgentObjects.ContainsKey($"{Ns}/direct"));

        Assert.Equal(["studio.apply_requested", "studio.applied"], _audit.Written.Where(a => a.Action.StartsWith("studio.appl")).Select(a => a.Action));
        var details = JsonSerializer.Serialize(_audit.Written.Select(a => a.Details));
        Assert.DoesNotContain("Answer refund questions", details); // never the instructions
        Assert.Contains("\"agent\":\"direct\"", details);
    }

    [Fact]
    public async Task BuildersAndEmployees_CannotApplyDirectly_TheyPropose()
    {
        var builder = await _builder.PostAsJsonAsync("/api/studio/apply", Draft("not-direct"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, builder.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.PostAsJsonAsync("/api/studio/apply", Draft("not-direct"), Ct)).StatusCode);
        Assert.Empty(_git.Pulls);
        Assert.Equal(2, _audit.Written.Count(a => a.Action == "studio.apply_refused"));

        // A builder's proposal merges only by a supervisor.
        var number = await ProposeAsync(_builder, Draft("reviewed"));
        Assert.Equal(HttpStatusCode.Forbidden, (await _builder.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsync($"/api/studio/proposals/{number}/merge", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task ASupervisorsDirectApply_OfATeamGrant_WaitsForTheOwner_WhoMayApplyIt()
    {
        Created(await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }, Ct));
        Created(await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" }, Ct));
        var team = new[] { new { scope = "team:billing", search = true, upsert = false } };

        var supervisor = await _supervisor.PostAsJsonAsync("/api/studio/apply", Draft("team-helper", grants: team), Ct);
        Assert.Equal(HttpStatusCode.Conflict, supervisor.StatusCode);
        Assert.Contains("waits for the owner", await supervisor.Content.ReadAsStringAsync(Ct));
        Assert.True(Assert.Single(_git.Pulls).Open);

        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsJsonAsync("/api/studio/apply", Draft("team-helper2", grants: team), Ct)).StatusCode);
    }

    // --- Deploy status from Argo CD (D363) ---

    [Fact]
    public async Task WithArgo_TheDeployStatus_IsArgosApplication()
    {
        var argo = await FakeArgo.StartAsync();
        _owned.Add(argo);
        await StartAsync(s =>
        {
            s["Studio:Argo:BaseUrl"] = argo.BaseUrl;
            s["Studio:Argo:Token"] = FakeArgo.Token;
        });
        argo.Applications["skanyxx-agents"] = FakeArgo.Application("someone-else", "Synced", "Healthy");
        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsJsonAsync("/api/studio/apply", Draft("argo-agent"), Ct)).StatusCode);
        var number = _git.Pulls.Single().Number;

        var waiting = (await ChangeAsync(_builder, number)).GetProperty("deploy");
        Assert.Equal("argo", waiting.GetProperty("source").GetString());
        Assert.Equal("Waiting", waiting.GetProperty("state").GetString());

        argo.Applications["skanyxx-agents"] = FakeArgo.Application("argo-agent", "OutOfSync", "Progressing", "Running");
        Assert.Equal("Syncing", (await ChangeAsync(_builder, number)).GetProperty("deploy").GetProperty("state").GetString());
        argo.Applications["skanyxx-agents"] = FakeArgo.Application("argo-agent", "Synced", "Degraded");
        Assert.Equal("Failed", (await ChangeAsync(_builder, number)).GetProperty("deploy").GetProperty("state").GetString());
        argo.Applications["skanyxx-agents"] = FakeArgo.Application("argo-agent", "Synced", "Healthy");
        Assert.Equal("Healthy", (await ChangeAsync(_builder, number)).GetProperty("deploy").GetProperty("state").GetString());
        Assert.NotEmpty(argo.Calls);

        argo.Applications.Clear(); // Argo answers 404: unknown, never an error page
        Assert.Equal("Unknown", (await ChangeAsync(_builder, number)).GetProperty("deploy").GetProperty("state").GetString());
        Assert.DoesNotContain(FakeArgo.Token, _console.ToString());
    }

    // --- CI validation (D362) ---

    [Fact]
    public async Task CiValidation_NeedsTheCiToken_AndJudgesFoldersLikeAMerge()
    {
        var draft = await TemplateDraftAsync(_builder, "research", "researcher");
        var preview = await (await _builder.PostAsJsonAsync("/api/studio/manifest", draft, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var body = new { name = "researcher", agentYaml = preview.GetProperty("agentYaml").GetString(), grantsYaml = preview.GetProperty("grantsYaml").GetString() };

        Assert.Equal(HttpStatusCode.Unauthorized, (await Ci(null, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ci("not-the-token", body)).StatusCode);
        // A person's session is not CI.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _owner.PostAsJsonAsync("/api/studio/ci/validate", body, Ct)).StatusCode);

        var valid = await (await Ci(CiToken, body)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(valid.GetProperty("valid").GetBoolean());

        var bomb = await (await Ci(CiToken, body with { agentYaml = "a: &x [1]\nb: *x\n" })).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(bomb.GetProperty("valid").GetBoolean());
        Assert.NotEmpty(bomb.GetProperty("problems").EnumerateArray());

        var leaked = body with { agentYaml = body.agentYaml!.Replace("You research topics", "Token gh" + "p_abcdefghijklmnopqrstuvwxyz0123456789AB. You research topics") };
        var secret = await (await Ci(CiToken, leaked)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains(secret.GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("an API token"));
        Assert.DoesNotContain(CiToken, _console.ToString());
    }

    [Fact]
    public async Task CiValidation_IsOff_WithoutACiToken()
    {
        await StartAsync(s => s["Studio:Ci:Token"] = "");
        Assert.Equal(HttpStatusCode.NotFound, (await Ci(CiToken, new { name = "x", agentYaml = "a: 1", grantsYaml = "b: 2" })).StatusCode);
    }

    [Fact]
    public async Task TheOwner_AddsTheCiWorkflow_AsOneMergedPullRequest()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _supervisor.PostAsync("/api/studio/ci/install", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync("/api/studio/ci/install", null, Ct)).StatusCode);

        var committed = File.ReadAllText(Repo.Path("docs", "templates", "agents-repo", "validate-agents.yaml"));
        Assert.Equal(committed, _git.Main()[".gitea/workflows/validate-agents.yaml"]);
        Assert.True(Assert.Single(_git.Pulls).Merged);
        Assert.Equal(HttpStatusCode.Conflict, (await _owner.PostAsync("/api/studio/ci/install", null, Ct)).StatusCode);
        Assert.Equal(["studio.ci_install_requested", "studio.ci_installed"], _audit.Written.Where(a => a.Action.StartsWith("studio.ci")).Select(a => a.Action));

        // The template text is served to studio people; the workflow in main does not disturb the reconciler.
        Assert.Equal(committed, await _builder.GetStringAsync("/api/studio/ci-template", Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await _employee.GetAsync("/api/studio/ci-template", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _supervisor.PostAsJsonAsync("/api/studio/apply", Draft("after-ci"), Ct)).StatusCode);
        var change = await ChangeAsync(_builder, 1);
        Assert.Contains("nothing to deploy", change.GetProperty("steps")[4].GetProperty("detail").GetString());
    }

    // --- The page (D360–D365) ---

    [Fact]
    public async Task ThePage_ShowsTheGallery_TheTimeline_AndTheRightButtonsPerRole()
    {
        await ProposeAsync(_builder, Draft("on-page"));
        var owner = await SignInAsync(HostApp.OwnerEmail, HostApp.OwnerPassword);
        var page = await (await owner.GetAsync("/Studio")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("data-template=\"support\"", page);
        Assert.Contains("data-locked", page);
        Assert.Contains("data-changes", page);
        Assert.Contains("data-timeline", page);
        Assert.Contains("data-apply", page);
        Assert.Contains("data-propose", page);
        Assert.Contains("data-install-ci", page);
        Assert.Contains("disabled=\"disabled\"", page); // the paid tool's checkbox

        var builder = await SignInAsync("bo@skanyxx.example", Password);
        var builderPage = await (await builder.GetAsync("/Studio?template=support")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("value=\"support-assistant\"", builderPage);
        Assert.Contains("data-propose", builderPage);
        Assert.DoesNotContain("data-apply", builderPage);
        Assert.DoesNotContain("data-install-ci", builderPage);
        var locked = await builder.GetAsync("/Studio?template=cloud-ops");
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
        Assert.Contains("Skanyxx:Licence:CloudTools", await locked.Content.ReadAsStringAsync(Ct));

        var employee = await SignInAsync("eve@skanyxx.example", Password);
        Assert.Equal(HttpStatusCode.Redirect, (await employee.GetAsync("/Studio")).StatusCode);
    }

    [Fact]
    public async Task NoToken_ReachesALogLine_AnAuditRow_OrAnAnswer()
    {
        await ProposeAsync(_builder, Draft("quiet"));
        await _supervisor.PostAsJsonAsync("/api/studio/apply", Draft("quiet2"), Ct);
        await _owner.PostAsync("/api/studio/ci/install", null, Ct);
        await Ci("wrong", new { name = "x", agentYaml = "", grantsYaml = "" });
        var answers = string.Join("\n",
            await _builder.GetStringAsync("/api/studio/changes", Ct),
            await _builder.GetStringAsync("/api/studio/options", Ct),
            await _builder.GetStringAsync("/api/studio/templates", Ct),
            await _builder.GetStringAsync("/api/studio/ci-template", Ct));
        var audit = JsonSerializer.Serialize(_audit.Written.Select(a => new { a.Action, a.TargetId, a.Details }));
        foreach (var token in new[] { FakeGitea.Token, CiToken })
        {
            Assert.DoesNotContain(token, _console.ToString());
            Assert.DoesNotContain(token, audit);
            Assert.DoesNotContain(token, answers);
        }
    }

    // --- helpers ---

    private static object Draft(string name, object[]? grants = null, object[]? tools = null) => new
    {
        name,
        description = "Answers refund questions",
        modelConfig = "default-model-config",
        instructions = "Answer refund questions from company memory.",
        skills = Array.Empty<string>(),
        mcpTools = tools ?? [],
        grants = grants ?? [new { scope = "company", search = true, upsert = false }],
        memoryTtlDays = (int?)null
    };

    private async Task<JsonObject> TemplateDraftAsync(HttpClient who, string template, string name)
    {
        var templates = await who.GetFromJsonAsync<JsonArray>("/api/studio/templates", Ct);
        var draft = (JsonObject)templates!.Single(t => t!["id"]!.GetValue<string>() == template)!["draft"]!.DeepClone();
        draft["name"] = name;
        return draft;
    }

    private async Task<int> ProposeAsync(HttpClient who, object draft)
    {
        var response = await who.PostAsJsonAsync("/api/studio/proposals", draft, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("number").GetInt32();
    }

    private async Task<JsonElement> ChangeAsync(HttpClient who, int number) =>
        (await who.GetFromJsonAsync<JsonElement>("/api/studio/changes", Ct)).EnumerateArray().Single(c => c.GetProperty("number").GetInt32() == number);

    private async Task<HttpResponseMessage> Ci(string? token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/studio/ci/validate") { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _host.Client().SendAsync(request, Ct);
    }

    private static void Created(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Created, response.StatusCode);

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

    private async Task<Browser> SignInAsync(string email, string password)
    {
        var browser = new Browser(_host);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password })).StatusCode);
        return browser;
    }

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

    public sealed class AuditProbe
    {
        public ConcurrentQueue<RecordAuditCommand> Written { get; } = new();
    }

    private sealed class ProbeAuditHandler(AuditProbe probe) : IRequestHandler<RecordAuditCommand>
    {
        public Task Handle(RecordAuditCommand command, CancellationToken ct)
        {
            probe.Written.Enqueue(command);
            return Task.CompletedTask;
        }
    }
}
