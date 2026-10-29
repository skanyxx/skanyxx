using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D527 (review Minor 3): the allow-lists skip their id routes, trusting the "why" that an ownership rule refuses anyone
/// else's. Here a supervisor owns one of each resource, and every allow-listed id route is called with that resource as
/// each role the list lets through: the answer must be 403 or 404, and the resource unchanged. The routes come from the
/// allow-lists, so a new id route fails here until it has a seeded resource or a reason in <see cref="NotOwnedByAPerson"/>.
/// </summary>
public sealed partial class RoleMatrixTests
{
    private const string Ticket = "SDB-58";
    private const string CardWhat = "FOREIGN-CARD-7c2e refunds wait for the Friday batch";
    private const string WorkflowName = "Foreign refunds";
    private const string SessionTitle = "Foreign refund question";

    /// <summary>A team of the employee and the builder, not the supervisor: a scope the callers may write, to lift or share into.</summary>
    private const string CallersTeam = "team:probers";

    /// <summary>Allow-listed id routes whose resource no person owns: there is no one else's to probe with.</summary>
    private static readonly Dictionary<string, string> NotOwnedByAPerson = new()
    {
        ["GET api/agents/{namespace}/{name}"] = "the agents catalog, the same for everyone the list lets read it",
        ["GET api/chat/agents/{namespace}/{name}"] = "an agent of the chat catalog, the same for every caller",
        ["GET api/studio/agents/{name}/draft"] = "an agent in main, the same for every builder",
        ["GET api/studio/proposals/{number}"] = "a pull request in skanyxx-agents every builder may read",
        ["POST api/studio/proposals/{number}/preview"] = "any builder may preview any open proposal (D030), without MCP tools (D392)",
        ["POST api/studio/proposals/{number}/chat"] = "a turn with that shared preview, without MCP tools (D392)",
        // The one Studio route with an owner; seeding it takes Studio set up on a fake Gitea and a reconcile pass, and
        // StudioTests already has another builder's close refused (403).
        ["POST api/studio/proposals/{number}/close"] = "its author's; covered by StudioTests (another builder's close is 403)",
        ["POST api/plugins/{name}/uninstall"] = "a catalog plugin; the uninstall is a pull request a supervisor merges",
    };

    private sealed record Foreign(string Session, string File, string Scope, string Key, string Workflow, string Run);

    [Fact]
    public async Task BelowSupervisor_AllowListedIdRoutes_RefuseASupervisorsResources()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var kagent = await FakeKAgent.StartAsync();
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["Skanyxx:RateLimit:PermitLimit"] = "100000";
            s["KAgent:BaseUrl"] = "127.0.0.1";
            s["KAgent:Port"] = kagent.Port.ToString();
        });
        var owner = await host.OwnerAsync();
        var (supervisor, supervisorId) = await TestPeople.InviteAsync(host, owner, "foreign-supervisor@skanyxx.example", SkanyxxRoles.Supervisor);
        var people = new Dictionary<string, (HttpClient Api, string Id)>
        {
            [SkanyxxRoles.Employee] = await TestPeople.InviteAsync(host, owner, "foreign-employee@skanyxx.example", SkanyxxRoles.Employee),
            [SkanyxxRoles.Builder] = await TestPeople.InviteAsync(host, owner, "foreign-builder@skanyxx.example", SkanyxxRoles.Builder)
        };
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "probing", name = "Probing" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "probers", name = "Probers", department = "probing" }, ct)).StatusCode);
        foreach (var (_, id) in people.Values)
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync($"/api/identity/org/teams/probers/members/{id}", null, ct)).StatusCode);
        var foreign = await SeedAsync(supervisor, supervisorId, ct);

        // An exclusion outlives its route only by mistake.
        Assert.All(NotOwnedByAPerson.Keys, k => Assert.True(BelowSupervisor.ContainsKey(k) || ReadsBelowSupervisor.ContainsKey(k), $"{k} is not allow-listed"));
        var problems = new List<string>();
        var probed = new List<string>();
        foreach (var (key, (roles, _)) in BelowSupervisor.Concat(ReadsBelowSupervisor).Where(e => Parameter().IsMatch(e.Key)).OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (NotOwnedByAPerson.ContainsKey(key))
                continue;
            foreach (var role in roles)
            {
                var client = people[role].Api;
                if (!Targets(foreign).TryGetValue(key, out var target))
                {
                    problems.Add($"{key}: no seeded resource; add one to Targets, or a reason to NotOwnedByAPerson");
                    continue;
                }
                using var response = await client.SendAsync(Request(key.Split(' ')[0], target.Path, target.Body), ct);
                var text = await response.Content.ReadAsStringAsync(ct);
                // The ticket view is a filtered read: it answers 200 for any ticket, and must name nothing it filtered out.
                var refused = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    || (key == "GET api/chat/tickets/{key}" && response.StatusCode == HttpStatusCode.OK && !text.Contains(foreign.Session, StringComparison.Ordinal));
                probed.Add($"{key} as {role}: {(int)response.StatusCode}");
                if (!refused)
                    problems.Add($"{key} as {role}: {(int)response.StatusCode} {text[..Math.Min(text.Length, 200)]}");
            }
        }

        Assert.True(problems.Count == 0, "A supervisor's resource reachable below supervisor:\n" + string.Join("\n", problems));
        Assert.NotEmpty(probed);
        TestContext.Current.SendDiagnosticMessage("Probed:\n" + string.Join("\n", probed));
        await AssertUnchangedAsync(owner, supervisor, foreign, ct);
        // The probes ran signed in: a 401 everywhere would have been refused for the wrong reason.
        foreach (var (client, _) in people.Values)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/me", ct)).StatusCode);
    }

    /// <summary>Each id route's call on the supervisor's resource; a write's body is what a caller would steal or change it with.</summary>
    private static Dictionary<string, (string Path, object? Body)> Targets(Foreign f)
    {
        var card = $"/api/memory/cards/{f.Scope}/{f.Key}";
        return new()
        {
            ["GET api/chat/conversations/{id}"] = ($"/api/chat/conversations/{f.Session}", null),
            ["PATCH api/chat/conversations/{id}"] = ($"/api/chat/conversations/{f.Session}", new { title = "taken" }),
            ["DELETE api/chat/conversations/{id}"] = ($"/api/chat/conversations/{f.Session}", null),
            ["GET api/chat/sessions/{id}"] = ($"/api/chat/sessions/{f.Session}", null),
            // Shared with the callers' team, the session would be theirs to read.
            ["PUT api/chat/sessions/{id}/scope"] = ($"/api/chat/sessions/{f.Session}/scope", new { scope = CallersTeam }),
            ["PUT api/chat/sessions/{id}/tickets"] = ($"/api/chat/sessions/{f.Session}/tickets", new { ticketKeys = new[] { "SDB-1" } }),
            ["DELETE api/chat/sessions/{id}"] = ($"/api/chat/sessions/{f.Session}", null),
            ["GET api/chat/tickets/{key}"] = ($"/api/chat/tickets/{Ticket}", null),
            ["GET api/chat/files/{id}"] = ($"/api/chat/files/{f.File}", null),
            ["DELETE api/chat/files/{id}"] = ($"/api/chat/files/{f.File}", null),
            ["GET api/memory/cards/{scope}/{key}"] = (card, null),
            ["GET api/memory/cards/{scope}/{key}/body"] = (card + "/body", null),
            ["GET api/memory/cards/{scope}/{key}/history"] = (card + "/history", null),
            ["PUT api/memory/cards/{scope}/{key}"] = (card, new { version = 1, type = "decision", what = "taken", why = "taken" }),
            // Lifted into the callers' team, the card would be theirs to read.
            ["POST api/memory/cards/{scope}/{key}/lift"] = (card + "/lift", new { targetScope = CallersTeam }),
            ["POST api/memory/cards/{scope}/{key}/rename"] = (card + "/rename", new { newKey = "taken", version = 1 }),
            ["POST api/memory/cards/{scope}/{key}/status"] = (card + "/status", new { version = 1, status = "stale" }),
            ["GET api/investigate/workflows/{id}"] = ($"/api/investigate/workflows/{f.Workflow}", null),
            ["PUT api/investigate/workflows/{id}"] = ($"/api/investigate/workflows/{f.Workflow}", WorkflowBody("Taken")),
            ["DELETE api/investigate/workflows/{id}"] = ($"/api/investigate/workflows/{f.Workflow}", null),
            ["POST api/investigate/workflows/{id}/duplicate"] = ($"/api/investigate/workflows/{f.Workflow}/duplicate", new { }),
            ["GET api/investigate/runs/{id}"] = ($"/api/investigate/runs/{f.Run}", null),
            ["GET api/investigate/runs/{id}/report.json"] = ($"/api/investigate/runs/{f.Run}/report.json", null),
            ["GET api/investigate/runs/{id}/report.pdf"] = ($"/api/investigate/runs/{f.Run}/report.pdf", null),
            ["POST api/investigate/runs/{id}/cancel"] = ($"/api/investigate/runs/{f.Run}/cancel", new { }),
            ["POST api/investigate/runs/{id}/rerun"] = ($"/api/investigate/runs/{f.Run}/rerun", new { }),
        };
    }

    /// <summary>The supervisor's chat on a ticket (kagent's conversation and Skanyxx's session), a file, a personal card, a workflow and its finished run.</summary>
    private static async Task<Foreign> SeedAsync(HttpClient supervisor, string supervisorId, CancellationToken ct)
    {
        var chat = await supervisor.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "seed", message = SessionTitle, ticketKeys = new[] { Ticket } }, ct);
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        var session = (await chat.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("conversationId").GetString()!;

        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent("foreign notes"u8.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(bytes, "file", "notes.txt");
        var upload = await supervisor.PostAsync("/api/chat/files", form, ct);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var file = (await upload.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var scope = $"personal:{supervisorId}";
        const string key = "foreign-note";
        Assert.Equal(HttpStatusCode.Created, (await supervisor.PutAsJsonAsync($"/api/memory/cards/{scope}/{key}",
            new { version = 0, type = "decision", what = CardWhat, why = "Finance policy" }, ct)).StatusCode);

        var created = await supervisor.PostAsJsonAsync("/api/investigate/workflows", WorkflowBody(WorkflowName), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workflow = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        var started = await supervisor.PostAsJsonAsync("/api/investigate/runs", new { workflowId = workflow, input = "why are refunds late?" }, ct);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var run = (await started.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        // Finished, so the reports exist and the probes do not race the run.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? state;
        do
        {
            await Task.Delay(100, ct);
            state = (await supervisor.GetFromJsonAsync<JsonElement>($"/api/investigate/runs/{run}", ct)).GetProperty("state").GetString();
        } while (state is "queued" or "running" && DateTime.UtcNow < deadline);
        Assert.Equal("succeeded", state);

        return new Foreign(session, file, scope, key, workflow, run);
    }

    /// <summary>Nothing the probes sent changed the supervisor's resources.</summary>
    private static async Task AssertUnchangedAsync(HttpClient owner, HttpClient supervisor, Foreign f, CancellationToken ct)
    {
        var session = await supervisor.GetFromJsonAsync<JsonElement>($"/api/chat/sessions/{f.Session}", ct);
        Assert.Equal(SessionTitle, session.GetProperty("title").GetString());
        Assert.NotEqual(CallersTeam, session.GetProperty("scope").GetString());
        Assert.Equal([Ticket], session.GetProperty("ticketKeys").EnumerateArray().Select(t => t.GetString()));
        // The ticket view the callers got without it does name it for its owner.
        Assert.Contains(f.Session, await supervisor.GetStringAsync($"/api/chat/tickets/{Ticket}", ct));
        Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync($"/api/chat/conversations/{f.Session}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync($"/api/chat/files/{f.File}", ct)).StatusCode);

        var card = await supervisor.GetFromJsonAsync<JsonElement>($"/api/memory/cards/{f.Scope}/{f.Key}", ct);
        Assert.Contains(CardWhat, card.GetRawText());
        Assert.Equal(1, card.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.GetAsync($"/api/memory/cards/{f.Scope}/taken", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/memory/cards/{CallersTeam}/{f.Key}", ct)).StatusCode);

        var workflow = await supervisor.GetFromJsonAsync<JsonElement>($"/api/investigate/workflows/{f.Workflow}", ct);
        Assert.Equal(WorkflowName, workflow.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync($"/api/investigate/runs/{f.Run}", ct)).StatusCode);
        // The owner sees every workflow and run: no duplicate and no rerun was made.
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/investigate/workflows", ct)).GetArrayLength());
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/investigate/runs", ct)).GetArrayLength());
    }

    private static object WorkflowBody(string name) => new
    {
        name,
        steps = new object[] { new { id = "s1", title = "Ask", kind = "agent", agent = "kagent/seed", prompt = "Look into {{input}}", steps = Array.Empty<object>() } }
    };

    private static HttpRequestMessage Request(string method, string path, object? body) =>
        new(new HttpMethod(method), path) { Content = body is null ? null : JsonContent.Create(body) };
}
