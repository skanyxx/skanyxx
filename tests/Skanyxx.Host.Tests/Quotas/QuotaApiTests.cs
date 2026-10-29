using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Data;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Host.Tests.Quotas;

/// <summary>
/// Token quotas end to end: chat turns count against the tightest limit, a person over it is refused with "Request
/// more", a request is approved within the approver's reach, usage is counted atomically, and every write is refused
/// server-side to anyone it is not theirs to make. Org: Finance (billing: sue the supervisor, ann, ben the builder) and
/// Sales (eu: tom the supervisor, eve).
/// </summary>
[Collection(HostCollection.Name)]
public sealed class QuotaApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long test passphrase";
    private const string Question = "What is the secret launch plan for Project Nightjar?";
    private const string Reason = "Quarter close needs the long reports";
    private const string Model = "kagent/default-model-config";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private string _database = "";
    private HttpClient _owner = null!, _sue = null!, _ann = null!, _ben = null!, _tom = null!, _eve = null!;
    private string _sueId = "", _annId = "", _eveId = "";

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _kagent.Usage = new JsonObject { ["promptTokenCount"] = 120, ["candidatesTokenCount"] = 30, ["totalTokenCount"] = 150 };
        _kagent.AgentObjects["kagent/helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "helper", ["namespace"] = "kagent", ["labels"] = new JsonObject { ["skanyxx.dev/merged"] = "true" } },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["modelConfig"] = "default-model-config" } }
        };
        _database = await fixture.NewDatabaseAsync();
        // Without sandboxes: demoting a supervisor stops their sandboxes (D392), and the default test AX is unreachable.
        _host = await HostApp.StartAsync(_database, s =>
        {
            s["KAgent:Port"] = _kagent.Port.ToString();
            HostApp.WithoutSandboxes(s);
        });
        _owner = await _host.OwnerAsync();
        _sue = await PersonAsync("sue@skanyxx.example", "supervisor");
        _ann = await PersonAsync("ann@skanyxx.example", "employee");
        _ben = await PersonAsync("ben@skanyxx.example", "builder");
        _tom = await PersonAsync("tom@skanyxx.example", "supervisor");
        _eve = await PersonAsync("eve@skanyxx.example", "employee");
        var people = (await _owner.GetFromJsonAsync<JsonElement>("/api/identity/people", Ct)).EnumerateArray().ToList();
        string Id(string name) => people.Single(p => p.GetProperty("email").GetString() == $"{name}@skanyxx.example").GetProperty("id").GetString()!;
        (_sueId, _annId, _eveId) = (Id("sue"), Id("ann"), Id("eve"));

        foreach (var (slug, name) in new[] { ("finance", "Finance"), ("sales", "Sales") })
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug, name }, Ct)).StatusCode);
        foreach (var (slug, name, department, members) in new[] { ("billing", "Billing", "finance", new[] { "sue", "ann", "ben" }), ("eu", "Sales EU", "sales", new[] { "tom", "eve" }) })
        {
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug, name, department }, Ct)).StatusCode);
            foreach (var member in members)
                Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsync($"/api/identity/org/teams/{slug}/members/{Id(member)}", null, Ct)).StatusCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _kagent.DisposeAsync();
    }

    private async Task<HttpClient> PersonAsync(string email, string role)
    {
        var invite = await _owner.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } }, Ct);
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        return _host.Client((await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tokens").GetProperty("accessToken").GetString()!);
    }

    private static Task<HttpResponseMessage> ChatAsync(HttpClient client, string message = Question) =>
        client.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "helper", message }, Ct);

    private static async Task<JsonElement> MeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/quotas/me", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("quota");
    }

    private static Task<HttpResponseMessage> SetLimitAsync(HttpClient client, string kind, string id, long? tokens) =>
        client.PutAsJsonAsync($"/api/quotas/limits/{kind}/{id}", new { monthlyTokens = tokens }, Ct);

    private static async Task<long> RequestAsync(HttpClient client, long amount)
    {
        var response = await client.PostAsJsonAsync("/api/quotas/requests", new { amount, reason = Reason }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> AuditAsync(string action) =>
        await _owner.GetFromJsonAsync<JsonElement>($"/api/identity/audit?action={action}", Ct);

    [Fact]
    public async Task ChatTurns_CountPromptAndCompletion_AndOverTheLimitChatRefusesWithRequestMore()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 200)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ChatAsync(_ann)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ChatAsync(_ann)).StatusCode); // 150 < 200: still allowed
        var refused = await ChatAsync(_ann);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Request more", await refused.Content.ReadAsStringAsync(Ct));
        var me = await MeAsync(_ann);
        Assert.Equal(240, me.GetProperty("promptTokens").GetInt64());
        Assert.Equal(60, me.GetProperty("completionTokens").GetInt64());
        Assert.Equal(300, me.GetProperty("used").GetInt64());
        Assert.Equal(150, me.GetProperty("percent").GetInt32());
        Assert.True(me.GetProperty("over").GetBoolean());
        Assert.Equal("user", me.GetProperty("bindingKind").GetString());
        Assert.Equal(0, (await MeAsync(_eve)).GetProperty("used").GetInt64()); // ann's turns are not eve's
    }

    [Fact]
    public async Task AStreamedTurn_CountsWhatKAgentReports()
    {
        var response = await _ann.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "helper", message = Question }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync(Ct);

        var me = await MeAsync(_ann);
        Assert.True(me.GetProperty("promptTokens").GetInt64() > 0);
        Assert.True(me.GetProperty("completionTokens").GetInt64() > 0);
        Assert.Equal(0, me.GetProperty("estimatedTurns").GetInt64());
    }

    [Fact]
    public async Task ATurnKAgentReportsNoUsageFor_IsEstimated_AndMarkedSo()
    {
        _kagent.Usage = null;

        Assert.Equal(HttpStatusCode.OK, (await ChatAsync(_ann)).StatusCode);

        var me = await MeAsync(_ann);
        Assert.Equal(1, me.GetProperty("estimatedTurns").GetInt64());
        Assert.True(me.GetProperty("estimatedTokens").GetInt64() >= Question.Length / 4);
        Assert.Equal(me.GetProperty("estimatedTokens").GetInt64(), me.GetProperty("used").GetInt64());
        Assert.Equal(0, me.GetProperty("promptTokens").GetInt64());
    }

    [Fact]
    public async Task TheTightestOfPersonTeamAndDepartment_Binds_AndIsNamed()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "department", "finance", 1000)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "team", "billing", 500)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 800)).StatusCode);

        var ann = await MeAsync(_ann);
        Assert.Equal(500, ann.GetProperty("limit").GetInt64());
        Assert.Equal("team", ann.GetProperty("bindingKind").GetString());
        Assert.Equal("Billing", ann.GetProperty("bindingName").GetString());
        Assert.Equal(800, ann.GetProperty("personalLimit").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await MeAsync(_eve)).GetProperty("limit").ValueKind); // Sales has no limit

        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "department", "finance", 100)).StatusCode);
        Assert.Equal("department", (await MeAsync(_ann)).GetProperty("bindingKind").GetString());
    }

    [Fact]
    public async Task ARequest_IsApprovedByTheTeamsSupervisor_AddsToTheLimit_AndIsAuditedWithNumbersOnly()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "team", "billing", 5000)).StatusCode); // the supervisor's room
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 200)).StatusCode);
        await ChatAsync(_ann);
        await ChatAsync(_ann);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ChatAsync(_ann)).StatusCode);

        var id = await RequestAsync(_ann, 1000);
        Assert.Equal(HttpStatusCode.Conflict, (await _ann.PostAsJsonAsync("/api/quotas/requests", new { amount = 5, reason = "again" }, Ct)).StatusCode);
        var overview = await _sue.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        Assert.Contains(overview.GetProperty("requests").EnumerateArray(), r => r.GetProperty("id").GetInt64() == id && r.GetProperty("reason").GetString() == Reason);

        var approved = await _sue.PostAsync($"/api/quotas/requests/{id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _owner.PostAsync($"/api/quotas/requests/{id}/deny", null, Ct)).StatusCode); // decided once

        var me = await _ann.GetFromJsonAsync<JsonElement>("/api/quotas/me", Ct);
        Assert.Equal(1200, me.GetProperty("quota").GetProperty("limit").GetInt64());
        Assert.Equal(1000, me.GetProperty("quota").GetProperty("extra").GetInt64());
        Assert.Equal("approved", me.GetProperty("requests")[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await ChatAsync(_ann)).StatusCode);

        var set = Assert.Single((await AuditAsync("quota.limit.set")).EnumerateArray(), r => r.GetProperty("details").GetProperty("kind").GetString() == "user");
        Assert.Equal(200, set.GetProperty("details").GetProperty("monthlyTokens").GetInt64());
        var requested = Assert.Single((await AuditAsync("quota.requested")).EnumerateArray());
        Assert.Equal(1000, requested.GetProperty("details").GetProperty("amount").GetInt64());
        var decided = Assert.Single((await AuditAsync("quota.request.approved")).EnumerateArray());
        Assert.Equal(_sueId, decided.GetProperty("actorId").GetString());
        Assert.Equal(_annId, decided.GetProperty("targetId").GetString());
        foreach (var row in new[] { set, requested, decided })
        {
            var text = row.GetRawText();
            Assert.DoesNotContain(Reason, text);
            Assert.DoesNotContain("Nightjar", text);
            Assert.DoesNotContain(_kagent.Answer, text);
        }
    }

    [Fact]
    public async Task ASupervisor_StaysWithinTheTeamQuota_TheOwnerDoesNot()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "team", "billing", 500)).StatusCode);

        // A personal 600 under the team's 500 raises nothing: the team still binds.
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_sue, "user", _annId, 600)).StatusCode);
        Assert.Equal(500, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_sue, "user", _annId, 400)).StatusCode);
        var id = await RequestAsync(_ann, 200); // 400 + 200 > 500
        Assert.Equal(HttpStatusCode.Forbidden, (await _sue.PostAsync($"/api/quotas/requests/{id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _sue.PostAsync($"/api/quotas/requests/{id}/deny", null, Ct)).StatusCode);
        Assert.Equal(400, (await MeAsync(_ann)).GetProperty("limit").GetInt64());

        var again = await RequestAsync(_ann, 200);
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync($"/api/quotas/requests/{again}/approve", null, Ct)).StatusCode);
        Assert.Equal(600, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 5000)).StatusCode);
    }

    [Fact]
    public async Task ASupervisor_CannotPassTheCap_ByRaisingOrClearingALimitAfterApprovingExtras()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "team", "billing", 500)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_sue, "user", _annId, 400)).StatusCode);
        var id = await RequestAsync(_ann, 100);
        Assert.Equal(HttpStatusCode.OK, (await _sue.PostAsync($"/api/quotas/requests/{id}/approve", null, Ct)).StatusCode); // 500: at the cap

        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", _annId, 500)).StatusCode); // 600 with the extra
        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", _annId, null)).StatusCode); // the team's 500 + 100
        Assert.Equal(500, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_sue, "user", _annId, 300)).StatusCode); // lowering is always allowed
        Assert.Equal(400, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
    }

    [Fact]
    public async Task ASupervisor_NeverActsOnTheOwnerOrAFellowSupervisor_InTheirTeam()
    {
        var people = (await _owner.GetFromJsonAsync<JsonElement>("/api/identity/people", Ct)).EnumerateArray().ToList();
        string Id(string email) => people.Single(p => p.GetProperty("email").GetString() == email).GetProperty("id").GetString()!;
        var (ownerId, tomId) = (Id(HostApp.OwnerEmail), Id("tom@skanyxx.example"));
        foreach (var member in new[] { ownerId, tomId })
            Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsync($"/api/identity/org/teams/billing/members/{member}", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", tomId, 100)).StatusCode);
        var toms = await RequestAsync(_tom, 50);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", ownerId, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", tomId, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sue.PostAsync($"/api/quotas/requests/{toms}/deny", null, Ct)).StatusCode);
        var overview = await _sue.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        Assert.DoesNotContain(overview.GetProperty("requests").EnumerateArray(), r => r.GetProperty("id").GetInt64() == toms);
        Assert.Equal(new[] { _annId, Id("ben@skanyxx.example") }.Order(StringComparer.Ordinal),
            overview.GetProperty("actFor").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal(JsonValueKind.Null, (await MeAsync(_owner)).GetProperty("limit").ValueKind);
    }

    [Fact]
    public async Task EveryWrite_IsRefusedServerSide_ToEmployeesBuildersOtherTeamsSupervisorsAndOneself()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 100)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _sueId, 100)).StatusCode);
        var annsRequest = await RequestAsync(_ann, 50);
        var suesRequest = await RequestAsync(_sue, 50);

        async Task Refused(HttpClient client, string who, bool asOwnerOnly = false)
        {
            var writes = new List<(string, Task<HttpResponseMessage>)>
            {
                ("team limit", SetLimitAsync(client, "team", "billing", 1)),
                ("department limit", SetLimitAsync(client, "department", "finance", 1)),
                ("price", client.PutAsJsonAsync($"/api/quotas/prices/kagent/default-model-config", new { pricePer1K = 1m }, Ct)),
            };
            if (!asOwnerOnly)
            {
                writes.Add(("ann's limit", SetLimitAsync(client, "user", _annId, 1_000_000)));
                writes.Add(("approve ann", client.PostAsync($"/api/quotas/requests/{annsRequest}/approve", null, Ct)));
                writes.Add(("deny ann", client.PostAsync($"/api/quotas/requests/{annsRequest}/deny", null, Ct)));
            }
            foreach (var (what, call) in writes)
                Assert.True((await call).StatusCode == HttpStatusCode.Forbidden, $"{who} could change {what}");
        }

        await Refused(_ann, "an employee");
        await Refused(_ben, "a builder");
        await Refused(_tom, "another team's supervisor");
        await Refused(_eve, "an employee of another team");
        await Refused(_sue, "the team's supervisor", asOwnerOnly: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await _ann.GetAsync("/api/quotas/overview", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _ben.GetAsync("/api/quotas/overview", Ct)).StatusCode);
        // A supervisor never acts on themselves.
        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", _sueId, 1_000_000)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sue.PostAsync($"/api/quotas/requests/{suesRequest}/approve", null, Ct)).StatusCode);
        // Another team's supervisor does not even see ann.
        var toms = await _tom.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        Assert.DoesNotContain(toms.GetProperty("people").EnumerateArray(), p => p.GetProperty("userId").GetString() == _annId);
        Assert.DoesNotContain(toms.GetProperty("requests").EnumerateArray(), r => r.GetProperty("id").GetInt64() == annsRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client().PutAsJsonAsync($"/api/quotas/limits/user/{_annId}", new { monthlyTokens = 1 }, Ct)).StatusCode);

        // Nothing changed.
        Assert.Equal(100, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
        var owners = await _owner.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        Assert.All(owners.GetProperty("requests").EnumerateArray(), r => Assert.Equal("pending", r.GetProperty("status").GetString()));
        Assert.Empty(owners.GetProperty("prices").EnumerateArray());
        Assert.DoesNotContain(owners.GetProperty("limits").EnumerateArray(), l => l.GetProperty("kind").GetString() != "user");
        Assert.Empty((await AuditAsync("quota.price.set")).EnumerateArray());
    }

    [Fact]
    public async Task TheOwnersPrices_GiveACostEstimate_AlwaysWithTheDisclaimer()
    {
        Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsJsonAsync("/api/quotas/prices/kagent/default-model-config", new { pricePer1K = 0.002m }, Ct)).StatusCode);
        await ChatAsync(_ann);
        await ChatAsync(_ann);

        var me = await _ann.GetFromJsonAsync<JsonElement>("/api/quotas/me", Ct);
        Assert.Equal(0.0006m, me.GetProperty("quota").GetProperty("cost").GetDecimal()); // 300 tokens × 0.002 / 1,000
        Assert.Equal(QuotaCostDisclaimer.Text, me.GetProperty("costDisclaimer").GetString());
        var price = Assert.Single((await AuditAsync("quota.price.set")).EnumerateArray());
        Assert.Equal(Model, price.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task UsageCounting_IsAtomic_UnderConcurrentTurns()
    {
        const int Turns = 60;
        var month = new DateOnly(2026, 10, 1);
        var options = new DbContextOptionsBuilder<QuotaDbContext>().UseNpgsql(_database).Options;

        await Task.WhenAll(Enumerable.Range(0, Turns).Select(async i =>
        {
            await using var db = new QuotaDbContext(options);
            await new QuotaStore(db).AddUsageAsync(month,
                new ChatQuotaUsage("ann", "kagent/helper", Model, $"c{i}", new ChatUsage(10, 5, 15), Estimated: i % 3 == 0), Ct);
        }));

        // A thinking model's total holds more than prompt plus candidates: the rest counts as output.
        await using (var db = new QuotaDbContext(options))
            await new QuotaStore(db).AddUsageAsync(month, new ChatQuotaUsage("bob", "kagent/helper", Model, "t", new ChatUsage(10, 5, 40), false), Ct);

        await using var read = new QuotaDbContext(options);
        Assert.Equal(30, Assert.Single(await new QuotaStore(read).UsageAsync(month, ["bob"], Ct)).CompletionTokens);
        var row = Assert.Single(await new QuotaStore(read).UsageAsync(month, ["ann"], Ct));
        Assert.Equal(Turns, row.Turns);
        Assert.Equal(Turns / 3, row.EstimatedTurns);
        Assert.Equal((Turns - Turns / 3) * 10, row.PromptTokens);
        Assert.Equal((Turns - Turns / 3) * 5, row.CompletionTokens);
        Assert.Equal(Turns / 3 * 15, row.EstimatedTokens);
    }

    [Fact]
    public async Task ThePages_ShowTheMeter_AndThePanelOnlyToOwnerAndSupervisors()
    {
        var owner = new Browser(_host);
        await owner.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });
        var panel = await owner.GetAsync("/Quotas");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);
        var html = await panel.Content.ReadAsStringAsync(Ct);
        Assert.Contains(QuotaCostDisclaimer.Title, html);
        Assert.Contains("id=\"setup-quotas\"", html);
        Assert.Contains("href=\"/Quotas\"", html);

        var ann = new Browser(_host);
        await ann.SubmitAsync("/Login", new() { ["Email"] = "ann@skanyxx.example", ["Password"] = Password });
        var denied = await ann.GetAsync("/Quotas");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/AccessDenied", new Uri(_host.BaseAddress, denied.Headers.Location!).AbsolutePath);
        var chat = await (await ann.GetAsync("/Chat")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("data-quota-meter=\"compact\"", chat);
        Assert.Contains("id=\"quotaRequestDialog\"", chat);
        Assert.DoesNotContain("href=\"/Quotas\"", chat);
        var account = await (await ann.GetAsync("/Account")).Content.ReadAsStringAsync(Ct);
        Assert.Contains("data-quota-meter=\"full\"", account);
    }

    // --- QA round 1 (D339, D380-D384) ---

    private static async Task<HttpResponseMessage> OpenStreamAsync(HttpClient client, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new { agentNamespace = "kagent", agentName = "helper", message = Question })
        };
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task ReadUntilAsync(HttpResponseMessage response, string line, CancellationToken ct)
    {
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } read && read != line) { }
    }

    private static async Task<bool> UntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
                return true;
            await Task.Delay(100, Ct);
        }
        return false;
    }

    [Fact]
    public async Task WithoutATeamOrDepartmentLimit_ASupervisorMayOnlyLowerThePersonalLimit_AndApprovesNothingAboveIt()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 1000)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", _annId, 2000)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", _annId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_sue, "user", _annId, 500)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_sue, "user", _annId, 800)).StatusCode); // the cap is the limit as it stands
        var id = await RequestAsync(_ann, 100);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sue.PostAsync($"/api/quotas/requests/{id}/approve", null, Ct)).StatusCode);
        Assert.Equal(500, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync($"/api/quotas/requests/{id}/approve", null, Ct)).StatusCode);
        Assert.Equal(600, (await MeAsync(_ann)).GetProperty("limit").GetInt64());

        // Nobody set any limit on eve: she is unlimited, so her supervisor can only give her one.
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_tom, "user", _eveId, 100)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetLimitAsync(_tom, "user", _eveId, null)).StatusCode);
    }

    [Fact]
    public async Task AStoppedStreamedTurn_CountsItsPromptOnce()
    {
        _kagent.StreamGate = new TaskCompletionSource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var response = await OpenStreamAsync(_ann, stop.Token);
        await ReadUntilAsync(response, "event: session", stop.Token);
        await stop.CancelAsync();
        response.Dispose();

        Assert.True(await UntilAsync(async () => (await MeAsync(_ann)).GetProperty("turns").GetInt64() > 0), "the stopped turn was not counted");
        await Task.Delay(500, Ct);
        var me = await MeAsync(_ann);
        Assert.Equal(1, me.GetProperty("turns").GetInt64());
        Assert.Equal(1, me.GetProperty("estimatedTurns").GetInt64());
        Assert.True(me.GetProperty("estimatedTokens").GetInt64() >= Question.Length / 4);
    }

    [Fact]
    public async Task AFailedStreamedTurn_CountsOnce()
    {
        _kagent.StreamFinalState = "failed";

        var response = await _ann.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "helper", message = Question }, Ct);
        Assert.Contains("event: error", await response.Content.ReadAsStringAsync(Ct));

        var me = await MeAsync(_ann);
        Assert.Equal(1, me.GetProperty("turns").GetInt64());
        Assert.True(me.GetProperty("used").GetInt64() >= Question.Length / 4);
    }

    [Fact]
    public async Task AStreamedMessageKAgentSendsTwice_CountsOnce()
    {
        _kagent.RepeatFinalMessage = true;

        var response = await _ann.PostAsJsonAsync("/api/chat/stream", new { agentNamespace = "kagent", agentName = "helper", message = Question }, Ct);
        Assert.Contains("event: done", await response.Content.ReadAsStringAsync(Ct));

        var me = await MeAsync(_ann);
        Assert.Equal(50, me.GetProperty("promptTokens").GetInt64());
        Assert.Equal(10, me.GetProperty("completionTokens").GetInt64());
    }

    [Fact]
    public async Task ABlockingTurnKAgentFails_CountsItsPrompt()
    {
        _kagent.A2AStatus = 500;

        Assert.Equal(HttpStatusCode.BadGateway, (await ChatAsync(_ann)).StatusCode);

        var me = await MeAsync(_ann);
        Assert.Equal(1, me.GetProperty("estimatedTurns").GetInt64());
        Assert.True(me.GetProperty("estimatedTokens").GetInt64() >= Question.Length / 4);
    }

    [Fact]
    public async Task ABlockingTurnTheCallerLeaves_IsCounted_AndKAgentsTaskIsCancelled()
    {
        _kagent.A2ADelay = TimeSpan.FromSeconds(20);
        using var leave = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var turn = _ann.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = "helper", message = Question }, leave.Token);
        Assert.True(await UntilAsync(() => Task.FromResult(!_kagent.Tasks.IsEmpty)), "the turn never reached kagent");
        await leave.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);

        Assert.True(await UntilAsync(() => Task.FromResult(_kagent.Tasks.Values.Single().State == "canceled")), "kagent's task was not cancelled");
        Assert.True(await UntilAsync(async () => (await MeAsync(_ann)).GetProperty("estimatedTurns").GetInt64() == 1), "the abandoned turn was not counted");
    }

    [Fact]
    public async Task APersonWithALimit_RunsAtMostTwoTurnsAtOnce()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 1_000_000)).StatusCode);
        _kagent.StreamGate = new TaskCompletionSource();
        using var first = await OpenStreamAsync(_ann, Ct);
        using var second = await OpenStreamAsync(_ann, Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var third = await ChatAsync(_ann);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Contains("answers on their way", await third.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await ChatAsync(_eve)).StatusCode); // eve has no limit: never held back

        _kagent.StreamGate.SetResult();
        await ReadUntilAsync(first, "event: done", Ct);
        await ReadUntilAsync(second, "event: done", Ct);
        Assert.True(await UntilAsync(async () => (await ChatAsync(_ann)).StatusCode == HttpStatusCode.OK), "the places were not given back");
    }

    [Fact]
    public async Task ConcurrentFirstWrites_OfALimitAndAPrice_AllSucceed()
    {
        var limits = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => SetLimitAsync(_owner, "team", "billing", i * 100)));
        var prices = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            _owner.PutAsJsonAsync("/api/quotas/prices/kagent/default-model-config", new { pricePer1K = i / 1000m }, Ct)));

        Assert.All(limits.Concat(prices), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var overview = await _owner.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        Assert.Single(overview.GetProperty("limits").EnumerateArray(), l => l.GetProperty("kind").GetString() == "team");
        Assert.Single(overview.GetProperty("prices").EnumerateArray());
    }

    [Fact]
    public async Task ASupervisor_SeesOnlyANameAndAPercentage_ForTeamMatesTheyMayNotActFor()
    {
        var people = (await _owner.GetFromJsonAsync<JsonElement>("/api/identity/people", Ct)).EnumerateArray().ToList();
        var tomId = people.Single(p => p.GetProperty("email").GetString() == "tom@skanyxx.example").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsync($"/api/identity/org/teams/billing/members/{tomId}", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", tomId, 1000)).StatusCode);
        await ChatAsync(_tom);

        var overview = await _sue.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        var tom = overview.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("userId").GetString() == tomId);
        Assert.True(tom.GetProperty("masked").GetBoolean());
        Assert.Equal(15, tom.GetProperty("percent").GetInt32());
        Assert.Equal("", tom.GetProperty("email").GetString());
        Assert.Equal(0, tom.GetProperty("used").GetInt64());
        Assert.Empty(tom.GetProperty("teams").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, tom.GetProperty("limit").ValueKind);
        Assert.DoesNotContain(overview.GetProperty("limits").EnumerateArray(), l => l.GetProperty("id").GetString() == tomId);
        var ann = overview.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("userId").GetString() == _annId);
        Assert.False(ann.GetProperty("masked").GetBoolean());
        Assert.Equal("ann@skanyxx.example", ann.GetProperty("email").GetString());
        var owners = await _owner.GetFromJsonAsync<JsonElement>("/api/quotas/overview", Ct);
        Assert.All(owners.GetProperty("people").EnumerateArray(), p => Assert.False(p.GetProperty("masked").GetBoolean()));
    }

    [Fact]
    public async Task ADemotedSupervisor_CanNoLongerChangeQuotas_WithTheSessionTheyHad()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_sue, "user", _annId, 100)).StatusCode);

        (await _owner.PutAsJsonAsync($"/api/identity/people/{_sueId}/roles", new { roles = new[] { "employee" } }, Ct)).EnsureSuccessStatusCode();

        // The security stamp is checked on every request (identity, StaleRoleTests): the old token is refused outright.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetLimitAsync(_sue, "user", _annId, 50)).StatusCode);
        Assert.Equal(100, (await MeAsync(_ann)).GetProperty("limit").GetInt64());
    }

    [Fact]
    public async Task MyQuota_NamesWhoDecidedARequest()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetLimitAsync(_owner, "user", _annId, 100)).StatusCode);
        var id = await RequestAsync(_ann, 50);
        Assert.Equal(HttpStatusCode.OK, (await _sue.PostAsync($"/api/quotas/requests/{id}/deny", null, Ct)).StatusCode);

        var me = await _ann.GetFromJsonAsync<JsonElement>("/api/quotas/me", Ct);
        Assert.Equal("sue@skanyxx.example", me.GetProperty("requests")[0].GetProperty("decidedBy").GetString());
    }

    [Fact]
    public async Task TheQuota_RollsOverAtTheUtcMonthBoundary_NotTheLocalOne()
    {
        var options = new DbContextOptionsBuilder<QuotaDbContext>().UseNpgsql(_database).Options;
        await using var db = new QuotaDbContext(options);
        var store = new QuotaStore(db);
        // 1 November 01:30 in Jerusalem is still 31 October in UTC.
        var clock = new QuotaClock(new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(2)));
        var quota = new DbChatQuota(new NoMembership(), new QuotaLedger(store), store, new QuotaInFlight(clock), clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DbChatQuota>.Instance);
        await store.SetLimitAsync(QuotaScopes.User, "zed", 100, "owner", clock.GetUtcNow(), Ct);
        await quota.RecordAsync(new ChatQuotaUsage("zed", "kagent/helper", Model, "c", new ChatUsage(80, 40, 120), false), Ct);

        using (var refused = await quota.AdmitAsync("zed", "kagent/helper", Ct))
        {
            Assert.NotNull(refused.Refusal);
            Assert.Contains("resets on 1 November (UTC)", refused.Refusal);
        }
        clock.Now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        using (var admitted = await quota.AdmitAsync("zed", "kagent/helper", Ct))
            Assert.Null(admitted.Refusal);
        Assert.Empty(await store.UsageAsync(new DateOnly(2026, 11, 1), ["zed"], Ct));
        Assert.Single(await store.UsageAsync(new DateOnly(2026, 10, 1), ["zed"], Ct));
    }

    private sealed class NoMembership : IOrgMembership
    {
        public Task<OrgMembership> ForUserAsync(string userId, CancellationToken ct) =>
            Task.FromResult(new OrgMembership(new HashSet<string>(), new HashSet<string>()));
    }
}
