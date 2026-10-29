using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D392, requirements §2 ("nobody who is not a supervisor can do harm"): an employee and a builder call every write route
/// this build maps — enumerated from the running host's endpoints, so a new one cannot be missed — and each is refused
/// (401, 403, or the 404 of nothing there) unless <see cref="BelowSupervisor"/> lets that role write it. D394: every API
/// read the same way against <see cref="ReadsBelowSupervisor"/>, and a route with a parameter must be refused by its roles
/// (401/403): a made-up id is a 404 from any handler, which says nothing about a real one. D400: an allow-listed route with
/// a parameter is probed with a real id of another person's resource instead. Razor page form posts are not API routes:
/// the page's own roles and antiforgery guard them.
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class RoleMatrixTests(PostgresFixture fixture)
{
    private static readonly string[] Everyone = [SkanyxxRoles.Employee, SkanyxxRoles.Builder];
    private static readonly string[] Builders = [SkanyxxRoles.Builder];

    /// <summary>
    /// The write routes a role below supervisor may reach, and why. Reviewed line by line: a route belongs here only when
    /// what it writes is the caller's own, needs a one-time credential, or is refused further in by a rule that is not a
    /// role (ownership, scope membership, a pull request a supervisor merges).
    /// </summary>
    private static readonly Dictionary<string, (string[] Roles, string Why)> BelowSupervisor = new()
    {
        // Anonymous by design (README "Anonymous"): each needs a one-time token or a password, and the credential posts
        // have their own rate limit. A signed-in caller gains nothing from them.
        ["POST api/identity/bootstrap"] = (Everyone, "owner setup; needs the bootstrap token"),
        ["POST api/identity/unlock"] = (Everyone, "break-glass owner unlock; needs the bootstrap token"),
        ["POST api/identity/sign-in"] = (Everyone, "a password sign-in"),
        ["POST api/identity/refresh"] = (Everyone, "needs the caller's own refresh token"),
        ["POST api/identity/invites/lookup"] = (Everyone, "needs an invite token"),
        ["POST api/identity/invites/accept"] = (Everyone, "needs an invite token"),
        ["POST api/identity/password/forgot"] = (Everyone, "mails a reset link to the address given; answers the same either way"),
        ["POST api/identity/password/lookup"] = (Everyone, "needs a reset token"),
        ["POST api/identity/password/reset"] = (Everyone, "needs a reset token"),

        // The caller's own.
        ["POST api/identity/sign-out"] = (Everyone, "ends the caller's own sessions (never called here: it would sign the matrix out)"),
        ["PUT api/me/preferences"] = (Everyone, "the caller's own preferences"),
        ["DELETE api/me/preferences"] = (Everyone, "the caller's own preferences"),
        ["POST api/quotas/requests"] = (Everyone, "asks a supervisor for more of the caller's own quota"),

        // Chat is what everyone is here for (spend bounded by quotas); every item is the caller's own in kagent.
        ["POST api/chat"] = (Everyone, "a chat turn as the caller"),
        ["POST api/chat/stream"] = (Everyone, "a chat turn as the caller"),
        ["POST api/chat/files"] = (Everyone, "a file for the caller's own conversation"),
        ["PATCH api/chat/conversations/{id}"] = (Everyone, "the caller's own conversation (kagent looks it up as the caller)"),
        ["DELETE api/chat/conversations/{id}"] = (Everyone, "the caller's own conversation (kagent looks it up as the caller)"),
        ["DELETE api/chat/cache"] = (Everyone, "the caller's own cached answers; all of them only for the owner (handler)"),
        ["PUT api/chat/sessions/{id}/scope"] = (Everyone, "the caller's own session; the target scope is checked like a memory write"),
        ["PUT api/chat/sessions/{id}/tickets"] = (Everyone, "the caller's own session; anyone else's is 404 or 403 (OwnSession)"),
        ["DELETE api/chat/sessions/{id}"] = (Everyone, "the caller's own session; anyone else's is 404 or 403 (OwnSession)"),
        ["DELETE api/chat/files/{id}"] = (Everyone, "the caller's own file; anyone else's is 404 (ChatFiles keys by owner)"),

        // Memory: what a person may write is decided per scope (AccessPolicy): personal and member teams, company only a supervisor.
        ["PUT api/memory/cards/{scope}/{key}"] = (Everyone, "scope rules: personal, member teams and departments; company needs a supervisor"),
        ["POST api/memory/cards/{scope}/{key}/lift"] = (Everyone, "scope rules on both ends, as a write into the target"),
        ["POST api/memory/cards/{scope}/{key}/rename"] = (Everyone, "scope rules, as a write"),
        ["POST api/memory/cards/{scope}/{key}/status"] = (Everyone, "scope rules, as a write"),

        // D312: proposing is a pull request in skanyxx-agents; nothing changes until a supervisor merges it.
        ["POST api/agents/proposals"] = (Everyone, "opens a pull request a supervisor merges"),

        // Builders draft (D022): previews run without MCP tools (D392), plugins become pull requests.
        ["POST api/studio/manifest"] = (Builders, "renders a draft as YAML; writes nothing"),
        ["POST api/studio/factory"] = (Builders, "drafts an agent with a model turn; writes nothing to kagent"),
        ["POST api/studio/proposals"] = (Builders, "opens a pull request a supervisor merges"),
        ["POST api/studio/proposals/{number}/preview"] = (Builders, "a throwaway preview without MCP tools (D392)"),
        ["POST api/studio/proposals/{number}/chat"] = (Builders, "a turn with that preview, without MCP tools (D392)"),
        ["POST api/studio/proposals/{number}/close"] = (Builders, "closes a pull request; the handler allows only its author or a supervisor"),
        ["POST api/plugins/inspect"] = (Builders, "reads a plugin from the catalog; writes nothing"),
        ["POST api/plugins/install"] = (Builders, "opens a pull request a supervisor merges"),
        ["POST api/plugins/{name}/uninstall"] = (Builders, "opens a pull request a supervisor merges"),
        ["POST api/investigate/runs"] = (Builders, "Investigate is the builders' panel; steps count against the starter's quota (D385)"),
        ["POST api/investigate/workflows"] = (Builders, "Investigate is the builders' panel; a workflow is its author's"),
        ["PUT api/investigate/workflows/{id}"] = (Builders, "Investigate is the builders' panel; a workflow is its author's"),
        ["DELETE api/investigate/workflows/{id}"] = (Builders, "its owner, the owner, or a supervisor of its owner's team; else 404/403 (WorkflowAccess)"),
        ["POST api/investigate/workflows/{id}/duplicate"] = (Builders, "a workflow the caller may see, copied as their own; else 404 (WorkflowAccess)"),
        ["POST api/investigate/runs/{id}/cancel"] = (Builders, "a run the caller may see: theirs, or their team's for a supervisor; else 404 (WorkflowAccess, D371)"),
        ["POST api/investigate/runs/{id}/rerun"] = (Builders, "a run the caller may see, started again as the caller; else 404 (WorkflowAccess, D371)"),
    };

    /// <summary>
    /// The API reads a role below supervisor may reach, and why. Reviewed line by line, like <see cref="BelowSupervisor"/>:
    /// a read belongs here only when what it returns is the caller's own, is filtered to what the caller may see by a rule
    /// that is not a role (ownership, sharing, scope membership, the starter's team), or is the catalog of the panels that
    /// role has (D394).
    /// </summary>
    private static readonly Dictionary<string, (string[] Roles, string Why)> ReadsBelowSupervisor = new()
    {
        // Anonymous by design: what the sign-in page needs before anyone is signed in.
        ["GET api/identity/status"] = (Everyone, "whether the install is set up and which sign-ins it offers; anonymous"),

        // The caller's own.
        ["GET api/identity/me"] = (Everyone, "the caller's own account"),
        ["GET api/me/preferences"] = (Everyone, "the caller's own preferences"),
        ["GET api/quotas/me"] = (Everyone, "the caller's own quota"),
        ["GET api/chat/limits"] = (Everyone, "the caller's own chat limits"),
        ["GET api/chat/conversations"] = (Everyone, "the caller's own conversations (kagent lists them as the caller)"),
        ["GET api/chat/conversations/{id}"] = (Everyone, "the caller's own conversation (kagent looks it up as the caller)"),
        ["GET api/chat/files/{id}"] = (Everyone, "the caller's own file; anyone else's is 404 (ChatFiles keys by owner)"),
        ["GET api/chat/cache"] = (Everyone, "counts of the caller's own cached answers; everyone's only for the owner (handler)"),

        // Chat is what everyone is here for: the agents a person can talk to, as the chat panel lists them.
        ["GET api/chat/agents"] = (Everyone, "the agents the caller can chat with"),
        ["GET api/chat/agents/{namespace}/{name}"] = (Everyone, "one agent the caller can chat with"),
        ["GET api/agents"] = (Everyone, "the agents catalog (endpoint roles name employee on purpose); no secrets, no tools' credentials"),
        ["GET api/agents/options"] = (Everyone, "the choices the agents catalog offers"),
        ["GET api/agents/setup"] = (Everyone, "whether agents are set up, for the empty state"),
        ["GET api/agents/{namespace}/{name}"] = (Everyone, "one agent of the catalog"),

        // Filtered by a rule that is not a role.
        ["GET api/chat/sessions"] = (Everyone, "only sessions the caller may read: their own and those shared with their teams (SessionAccess)"),
        ["GET api/chat/sessions/{id}"] = (Everyone, "only a session the caller may read; anyone else's is 404 (SessionAccess)"),
        ["GET api/chat/tickets/{key}"] = (Everyone, "only readable sessions; the ticket's runs only for supervisors (D394)"),
        ["GET api/analytics/usage"] = (Everyone, "the caller's own usage; a team or the company only where AnalyticsViewer reaches"),
        ["GET api/analytics/scopes"] = (Everyone, "the drill-down the caller may open (just \"Me\" for most people)"),
        ["GET api/memory/cards"] = (Everyone, "searches only the scopes AccessPolicy lets the caller read"),
        ["GET api/memory/cards/{scope}/{key}"] = (Everyone, "a card in a scope the caller may read; else 403/404 (AccessPolicy)"),
        ["GET api/memory/cards/{scope}/{key}/body"] = (Everyone, "a card in a scope the caller may read; else 403/404 (AccessPolicy)"),
        ["GET api/memory/cards/{scope}/{key}/history"] = (Everyone, "a card in a scope the caller may read; else 403/404 (AccessPolicy)"),
        ["GET api/memory/library"] = (Everyone, "the scopes AccessPolicy lets the caller read"),

        // Builders' panels (NavCatalog: Studio, Plugins, Investigate).
        ["GET api/studio"] = (Builders, "Studio is the builders' panel: open proposals are pull requests in skanyxx-agents"),
        ["GET api/studio/options"] = (Builders, "the choices the Studio form offers"),
        ["GET api/studio/templates"] = (Builders, "the Studio's starting templates"),
        ["GET api/studio/ci-template"] = (Builders, "the CI workflow a supervisor installs; holds no secret"),
        ["GET api/studio/changes"] = (Builders, "each change's pull request, checks, merge and deploy (D365)"),
        ["GET api/studio/agents/{name}/draft"] = (Builders, "an agent in main as a form, to propose a change (D2)"),
        ["GET api/studio/proposals/{number}"] = (Builders, "a pull request in skanyxx-agents"),
        ["GET api/plugins"] = (Builders, "Plugins is the builders' panel"),
        ["GET api/plugins/catalog"] = (Builders, "the plugin catalog"),
        ["GET api/investigate/options"] = (Builders, "Investigate is the builders' panel: the agents and teams a step may use"),
        ["GET api/investigate/workflows"] = (Builders, "only workflows the caller may see: their own and their teams' (WorkflowAccess)"),
        ["GET api/investigate/workflows/{id}"] = (Builders, "only a workflow the caller may see; else 404 (WorkflowAccess)"),
        ["GET api/investigate/runs"] = (Builders, "only runs the caller may see: theirs, or their team's for a supervisor (WorkflowAccess, D371)"),
        ["GET api/investigate/runs/{id}"] = (Builders, "only a run the caller may see; else 404 (WorkflowAccess, D371)"),
        ["GET api/investigate/runs/{id}/report.json"] = (Builders, "only a run the caller may see; else 404 (WorkflowAccess, D371)"),
        ["GET api/investigate/runs/{id}/report.pdf"] = (Builders, "only a run the caller may see; else 404 (WorkflowAccess, D371)"),
    };

    /// <summary>
    /// The allow-listed routes with a parameter whose resource belongs to no person, so there is no other person's id to
    /// try: <see cref="AnAllowListedIdRoute_RefusesAnotherPersonsResource"/> probes every other one.
    /// </summary>
    private static readonly Dictionary<string, string> NobodysOwn = new()
    {
        ["GET api/chat/agents/{namespace}/{name}"] = "an agent of the chat catalog, the same for everyone who may chat with it",
        ["GET api/agents/{namespace}/{name}"] = "an agent of the catalog; what a role sees of it is AgentAccess's, not ownership",
        ["GET api/studio/agents/{name}/draft"] = "an agent in main, the company's, which any builder may propose to change",
        ["GET api/studio/proposals/{number}"] = "every open proposal is every builder's to review, as GET api/studio lists them",
        ["POST api/studio/proposals/{number}/preview"] = "any builder may preview any open proposal, without MCP tools (D392)",
        ["POST api/studio/proposals/{number}/chat"] = "any builder may try any open proposal's preview, without MCP tools (D392)",
        ["POST api/plugins/{name}/uninstall"] = "an installed plugin is the company's; uninstalling opens a pull request a supervisor merges",
    };

    private static readonly HashSet<string> Writes = ["POST", "PUT", "PATCH", "DELETE"];
    private static readonly HashSet<string> Reads = ["GET"];

    [Fact]
    public async Task BelowSupervisor_EveryWriteOutsideTheAllowList_IsRefused() =>
        await AssertRefusedAsync("writes", host => Routes(host, Writes), BelowSupervisor);

    /// <summary>D394: the same for every API read, so a panel's data cannot be read around its page's roles.</summary>
    [Fact]
    public async Task BelowSupervisor_EveryReadOutsideTheAllowList_IsRefused() =>
        await AssertRefusedAsync("reads", host => [.. Routes(host, Reads).Where(r => r.Pattern.StartsWith("api/", StringComparison.Ordinal))], ReadsBelowSupervisor);

    [Fact]
    public void TheAllowLists_NameOnlyRoutesThatExist()
    {
        var writes = Routes(fixture.Host, Writes).Select(r => r.Key).ToHashSet();
        var reads = Routes(fixture.Host, Reads).Select(r => r.Key).ToHashSet();

        Assert.All(BelowSupervisor.Keys, k => Assert.Contains(k, writes));
        Assert.All(ReadsBelowSupervisor.Keys, k => Assert.Contains(k, reads));
    }

    /// <summary>
    /// D400 (security review Minor 3): an allow-listed route with a parameter is let in by its roles, so only the handler
    /// keeps one person off another's resource. Each is called as every allow-listed role below supervisor with a real id
    /// of a supervisor's run, workflow, conversation, session, file, card or proposal, and must answer 403 or 404; a list
    /// filtered to what the caller may read may answer 200, without the supervisor's item in it. Reads go first and
    /// deletes last, so a write that wrongly lands cannot hide a later probe's answer behind a 404.
    /// </summary>
    [Fact]
    public async Task AnAllowListedIdRoute_RefusesAnotherPersonsResource()
    {
        await using var kagent = await FakeKAgent.StartAsync();
        await using var git = await FakeGitea.StartAsync();
        var database = await fixture.NewDatabaseAsync();
        await using var host = await HostApp.StartAsync(database, s =>
        {
            s["Skanyxx:RateLimit:PermitLimit"] = "100000";
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = kagent.Port.ToString();
            s["Studio:Git:BaseUrl"] = git.BaseUrl;
            s["Studio:Git:Token"] = FakeGitea.Token;
            s["Studio:ReconcileSeconds"] = "3600";
        });
        var owner = await host.OwnerAsync();
        var (supervisor, supervisorId) = await TestPeople.InviteAsync(host, owner, "ids-supervisor@skanyxx.example", SkanyxxRoles.Supervisor);
        var people = new Dictionary<string, (HttpClient Client, string Id)>
        {
            [SkanyxxRoles.Employee] = await TestPeople.InviteAsync(host, owner, "ids-employee@skanyxx.example", SkanyxxRoles.Employee),
            [SkanyxxRoles.Builder] = await TestPeople.InviteAsync(host, owner, "ids-builder@skanyxx.example", SkanyxxRoles.Builder)
        };
        // A team both callers write to, so a lift's target is theirs and only its source can refuse it.
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "probes", name = "Probes" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "probes", name = "Probes", department = "probes" }, ct)).StatusCode);
        foreach (var (_, id) in people.Values)
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync($"/api/identity/org/teams/probes/members/{id}", null, ct)).StatusCode);
        await WaitForStudioAsync(kagent);
        var probes = IdProbes(await SeedAsync(database, git, supervisor, supervisorId));

        var idRoutes = BelowSupervisor.Concat(ReadsBelowSupervisor).Where(r => Parameter().IsMatch(r.Key)).ToList();
        Assert.Equal(idRoutes.Select(r => r.Key).Order(StringComparer.Ordinal),
            probes.Keys.Concat(NobodysOwn.Keys).Order(StringComparer.Ordinal));
        // The seeds are real: the supervisor is let through to each of them (a stored body then meets the missing object store).
        foreach (var (key, probe) in probes.Where(p => p.Key.StartsWith("GET ", StringComparison.Ordinal)))
        {
            using var own = await supervisor.SendAsync(probe.Request(key, supervisorId), TestContext.Current.CancellationToken);
            Assert.True(own.IsSuccessStatusCode || (key.EndsWith("/body", StringComparison.Ordinal) && own.StatusCode == HttpStatusCode.BadGateway),
                $"{key} as its supervisor: {(int)own.StatusCode}");
        }

        var problems = new List<string>();
        foreach (var (key, roles) in idRoutes.Where(r => probes.ContainsKey(r.Key)).Select(r => (r.Key, r.Value.Roles))
            .OrderBy(r => r.Key.Split(' ')[0] switch { "GET" => 0, "DELETE" => 2, _ => 1 }).ThenBy(r => r.Key, StringComparer.Ordinal))
            foreach (var role in roles)
            {
                var (client, id) = people[role];
                var probe = probes[key];
                using var response = await client.SendAsync(probe.Request(key, id), TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                var refused = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    || (probe.Hidden is { } hidden && response.IsSuccessStatusCode && !body.Contains(hidden, StringComparison.Ordinal));
                if (!refused)
                    problems.Add($"{key} as {role}: {(int)response.StatusCode}");
            }

        Assert.True(problems.Count == 0, "Another person's resource reachable below supervisor:\n" + string.Join("\n", problems));
    }

    private sealed record Seeded(string Conversation, string File, string Card, int CardVersion, string Workflow, string Run, int Pull, string Ticket);

    /// <summary>
    /// One request per allow-listed id route, on the supervisor's resource. <see cref="Body"/> is what the caller would
    /// send for their own (a valid body, so a refusal is the rules' and not validation's 400), given the caller's id.
    /// </summary>
    private sealed record IdProbe(string Path, Func<string, object>? Body = null, string? Hidden = null)
    {
        public HttpRequestMessage Request(string key, string callerId)
        {
            var method = key.Split(' ')[0];
            var request = new HttpRequestMessage(new HttpMethod(method), Path);
            if (Body is not null)
                request.Content = JsonContent.Create(Body(callerId));
            else if (method is not ("GET" or "DELETE"))
                request.Content = JsonContent.Create(new { });
            return request;
        }
    }

    private static Dictionary<string, IdProbe> IdProbes(Seeded s)
    {
        var card = $"/api/memory/cards/{s.Card}";
        object Steps() => new object[] { new { id = "s1", title = "Ask", kind = "agent", agent = "kagent/seed", prompt = "{{input}}", steps = Array.Empty<object>() } };
        return new()
        {
            ["GET api/chat/conversations/{id}"] = new($"/api/chat/conversations/{s.Conversation}"),
            ["PATCH api/chat/conversations/{id}"] = new($"/api/chat/conversations/{s.Conversation}", _ => new { title = "taken" }),
            ["DELETE api/chat/conversations/{id}"] = new($"/api/chat/conversations/{s.Conversation}"),
            ["GET api/chat/sessions/{id}"] = new($"/api/chat/sessions/{s.Conversation}"),
            ["PUT api/chat/sessions/{id}/scope"] = new($"/api/chat/sessions/{s.Conversation}/scope", id => new { scope = $"personal:{id}" }),
            ["PUT api/chat/sessions/{id}/tickets"] = new($"/api/chat/sessions/{s.Conversation}/tickets", _ => new { ticketKeys = new[] { "DEMO-2" } }),
            ["DELETE api/chat/sessions/{id}"] = new($"/api/chat/sessions/{s.Conversation}"),
            ["GET api/chat/tickets/{key}"] = new($"/api/chat/tickets/{s.Ticket}", Hidden: s.Conversation),
            ["GET api/chat/files/{id}"] = new($"/api/chat/files/{s.File}"),
            ["DELETE api/chat/files/{id}"] = new($"/api/chat/files/{s.File}"),
            ["GET api/memory/cards/{scope}/{key}"] = new(card),
            ["GET api/memory/cards/{scope}/{key}/body"] = new($"{card}/body"),
            ["GET api/memory/cards/{scope}/{key}/history"] = new($"{card}/history"),
            ["PUT api/memory/cards/{scope}/{key}"] = new(card, _ => new { version = s.CardVersion, type = "decision", what = "Taken over", why = "Probe" }),
            ["POST api/memory/cards/{scope}/{key}/lift"] = new($"{card}/lift", _ => new { targetScope = "team:probes" }),
            ["POST api/memory/cards/{scope}/{key}/rename"] = new($"{card}/rename", _ => new { newKey = "taken-over", version = s.CardVersion }),
            ["POST api/memory/cards/{scope}/{key}/status"] = new($"{card}/status", _ => new { version = s.CardVersion, status = "stale" }),
            ["POST api/studio/proposals/{number}/close"] = new($"/api/studio/proposals/{s.Pull}/close"),
            ["GET api/investigate/workflows/{id}"] = new($"/api/investigate/workflows/{s.Workflow}"),
            ["PUT api/investigate/workflows/{id}"] = new($"/api/investigate/workflows/{s.Workflow}", _ => new { name = "Taken", steps = Steps() }),
            ["POST api/investigate/workflows/{id}/duplicate"] = new($"/api/investigate/workflows/{s.Workflow}/duplicate"),
            ["DELETE api/investigate/workflows/{id}"] = new($"/api/investigate/workflows/{s.Workflow}"),
            ["GET api/investigate/runs/{id}"] = new($"/api/investigate/runs/{s.Run}"),
            ["GET api/investigate/runs/{id}/report.json"] = new($"/api/investigate/runs/{s.Run}/report.json"),
            ["GET api/investigate/runs/{id}/report.pdf"] = new($"/api/investigate/runs/{s.Run}/report.pdf"),
            ["POST api/investigate/runs/{id}/cancel"] = new($"/api/investigate/runs/{s.Run}/cancel"),
            ["POST api/investigate/runs/{id}/rerun"] = new($"/api/investigate/runs/{s.Run}/rerun"),
        };
    }

    /// <summary>The supervisor's own resources, made through the API as the supervisor would.</summary>
    private static async Task<Seeded> SeedAsync(string database, FakeGitea git, HttpClient supervisor, string supervisorId)
    {
        var ct = TestContext.Current.CancellationToken;
        var chat = await supervisor.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = "A private question" }, ct);
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        var conversation = (await chat.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("conversationId").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await supervisor.PutAsJsonAsync($"/api/chat/sessions/{conversation}/tickets", new { ticketKeys = new[] { "DEMO-1" } }, ct)).StatusCode);

        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent("Private notes"u8.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(bytes, "file", "notes.txt");
        var upload = await supervisor.PostAsync("/api/chat/files", form, ct);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var file = (await upload.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var card = $"personal:{supervisorId}/private-card";
        var written = await supervisor.PutAsJsonAsync($"/api/memory/cards/{card}", new { version = 0, type = "decision", what = "A private note", why = "Mine" }, ct);
        Assert.Equal(HttpStatusCode.Created, written.StatusCode);
        var cardVersion = (await written.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("version").GetInt32();
        // A body only has a route of its own once it lives in the object store, as LibraryPageTests stores one.
        await using (var db = new NpgsqlConnection(database))
        {
            await db.OpenAsync(ct);
            await using var stored = new NpgsqlCommand(
                "UPDATE memory_cards SET body = 'The excerpt', source = 's3://skanyxx-memory/cards/sha256/" + new string('a', 64) + "' WHERE key = 'private-card'", db);
            Assert.Equal(1, await stored.ExecuteNonQueryAsync(ct));
        }

        var created = await supervisor.PostAsJsonAsync("/api/investigate/workflows", new
        {
            name = "Private",
            steps = new object[] { new { id = "s1", title = "Ask", kind = "agent", agent = "kagent/seed", prompt = "{{input}}", steps = Array.Empty<object>() } }
        }, ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workflow = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        var started = await supervisor.PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow, input = "why?" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var run = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        var until = DateTime.UtcNow.AddSeconds(30);
        while ((await supervisor.GetFromJsonAsync<JsonElement>($"/api/investigate/runs/{run}", ct)).GetProperty("state").GetString() is "queued" or "running")
        {
            Assert.True(DateTime.UtcNow < until, "the supervisor's run did not finish");
            await Task.Delay(100, ct);
        }

        var pull = git.OpenPull("studio/private", "Private proposal", $"Agent: private\nProposed-by: {supervisorId}\n\nMine.");
        Assert.Contains((await supervisor.GetFromJsonAsync<JsonElement>("/api/studio", ct)).GetProperty("open").EnumerateArray(),
            p => p.GetProperty("number").GetInt32() == pull);
        return new Seeded(conversation, file, card, cardVersion, workflow, run, pull, "DEMO-1");
    }

    /// <summary>The studio sets its repo up on its first pass; a proposal opened before that would not be found.</summary>
    private static async Task WaitForStudioAsync(FakeKAgent kagent)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!kagent.AgentObjects.ContainsKey("kagent/skanyxx-factory"))
        {
            Assert.True(DateTime.UtcNow < until, "the studio's first pass did not finish");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Every route outside the allow-list is refused as an employee and as a builder. A route with a parameter must be
    /// refused by its roles (401 or 403), before the handler runs: the probe's made-up id would make any handler answer
    /// 404, so a 404 there proves nothing about who may act on a real one (D394).
    /// </summary>
    private async Task AssertRefusedAsync(string matrix, Func<HostApp, List<Route>> routes, Dictionary<string, (string[] Roles, string Why)> allowList)
    {
        await using var host = await HostApp.StartAsync(fixture.ConnectionString, s => s["Skanyxx:RateLimit:PermitLimit"] = "100000");
        var owner = await host.OwnerAsync();
        var people = new Dictionary<string, HttpClient>
        {
            [SkanyxxRoles.Employee] = (await TestPeople.InviteAsync(host, owner, $"{matrix}-employee@skanyxx.example", SkanyxxRoles.Employee)).Api,
            [SkanyxxRoles.Builder] = (await TestPeople.InviteAsync(host, owner, $"{matrix}-builder@skanyxx.example", SkanyxxRoles.Builder)).Api
        };

        var problems = new List<string>();
        foreach (var route in routes(host))
            foreach (var (role, client) in people)
            {
                if (allowList.TryGetValue(route.Key, out var allowed) && allowed.Roles.Contains(role))
                    continue;
                using var response = await client.SendAsync(Probe(route), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
                var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    || (response.StatusCode == HttpStatusCode.NotFound && !Parameter().IsMatch(route.Pattern));
                if (!refused)
                    problems.Add($"{route.Key} as {role}: {(int)response.StatusCode}");
            }

        Assert.True(problems.Count == 0, "Reachable below supervisor:\n" + string.Join("\n", problems));
        // Nothing above signed them out: a 401 everywhere would pass the matrix without testing it.
        foreach (var client in people.Values)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    private sealed record Route(string Method, string Pattern, bool Multipart)
    {
        public string Key => $"{Method} {Pattern}";
    }

    private static List<Route> Routes(HostApp host, HashSet<string> methods) =>
        [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Where(methods.Contains)
                .Select(m => new Route(m, e.RoutePattern.RawText!.TrimStart('/'),
                    e.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true)))
            .Distinct()
            .OrderBy(r => r.Pattern, StringComparer.Ordinal).ThenBy(r => r.Method, StringComparer.Ordinal)];

    /// <summary>A request the route's content type admits, so a refusal is the rules' and not routing's (415).</summary>
    private static HttpRequestMessage Probe(Route route)
    {
        var request = new HttpRequestMessage(new HttpMethod(route.Method), "/" + Fill(route.Pattern));
        if (route.Multipart)
        {
            var file = new ByteArrayContent([0x50, 0x4B, 0x05, 0x06]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            request.Content = new MultipartFormDataContent { { file, "file", "probe.zip" } };
        }
        else if (route.Method is not ("DELETE" or "GET"))
            request.Content = JsonContent.Create(new { });
        return request;
    }

    /// <summary>
    /// A value each parameter binds: the company scope for a memory scope, else a GUID, which is also a valid slug, name
    /// and key, so a refusal comes from the rules and not from binding.
    /// </summary>
    private static string Fill(string pattern) =>
        Parameter().Replace(pattern, m => m.Groups["name"].Value == "scope" ? "company" : "3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    [GeneratedRegex(@"\{\*{0,2}(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}]*)?\??\}")]
    private static partial Regex Parameter();
}
