using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Analytics;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>
/// The Dashboard and Analytics end to end: chat turns land in the rollup with kagent's reported tokens, the scopes are
/// enforced server-side (a user only themselves, a supervisor their team, the owner everything), the export is the
/// owner's and audited and carries no chat text, ranges are bounded, and the health tiles never show internals.
/// Org: team "billing" in "finance" = sue (supervisor) and ann; eve is in no team.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class AnalyticsApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long test passphrase";
    private const string Question = "What is the secret launch plan for Project Nightjar?";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeKAgent _kagent = null!;
    private HostApp _host = null!;
    private HttpClient _owner = null!, _sue = null!, _ann = null!, _eve = null!;
    private string _annId = "", _eveId = "";

    public async ValueTask InitializeAsync()
    {
        _kagent = await FakeKAgent.StartAsync();
        _kagent.Usage = new JsonObject { ["promptTokenCount"] = 120, ["candidatesTokenCount"] = 30, ["totalTokenCount"] = 150 };
        _kagent.AgentObjects["kagent/helper"] = new JsonObject
        {
            ["metadata"] = new JsonObject { ["name"] = "helper", ["namespace"] = "kagent", ["labels"] = new JsonObject { ["skanyxx.dev/merged"] = "true" } },
            ["spec"] = new JsonObject { ["type"] = "Declarative", ["declarative"] = new JsonObject { ["modelConfig"] = "default-model-config" } }
        };
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s =>
        {
            s["KAgent:Port"] = _kagent.Port.ToString();
            s["Analytics:Prices:kagent/default-model-config:InputPerMillion"] = "1";
            s["Analytics:Prices:kagent/default-model-config:OutputPerMillion"] = "4";
        });
        _owner = await _host.OwnerAsync();
        _sue = await PersonAsync("sue@skanyxx.example", "supervisor");
        _ann = await PersonAsync("ann@skanyxx.example", "employee");
        _eve = await PersonAsync("eve@skanyxx.example", "employee");
        var people = (await _owner.GetFromJsonAsync<JsonElement>("/api/identity/people", Ct)).EnumerateArray().ToList();
        string Id(string email) => people.Single(p => p.GetProperty("email").GetString() == email).GetProperty("id").GetString()!;
        (_annId, _eveId) = (Id("ann@skanyxx.example"), Id("eve@skanyxx.example"));

        Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" }, Ct)).StatusCode);
        foreach (var id in new[] { Id("sue@skanyxx.example"), _annId })
            Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsync($"/api/identity/org/teams/billing/members/{id}", null, Ct)).StatusCode);
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

    private static async Task<JsonElement> ChatAsync(HttpClient client, string agent, string message, string? conversationId = null)
    {
        var response = await client.PostAsJsonAsync("/api/chat", new { agentNamespace = "kagent", agentName = agent, message, conversationId }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> UsageAsync(HttpClient client, string query = "scope=me")
    {
        var response = await client.GetAsync($"/api/analytics/usage?{query}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static long Total(JsonElement report, string field) => report.GetProperty("totals").GetProperty(field).GetInt64();

    [Fact]
    public async Task ChatTurns_AreRolledUp_WithKAgentsTokens_PerPersonAgentAndModel()
    {
        var first = await ChatAsync(_ann, "helper", Question);
        await ChatAsync(_ann, "helper", "And the date?", first.GetProperty("conversationId").GetString());
        await ChatAsync(_eve, "seed", "Hello");
        _kagent.A2AStatus = 500;
        Assert.Equal(HttpStatusCode.BadGateway, (await _ann.PostAsJsonAsync("/api/chat",
            new { agentNamespace = "kagent", agentName = "helper", message = "again" }, Ct)).StatusCode);
        _kagent.A2AStatus = null;

        var ann = await UsageAsync(_ann);
        Assert.Equal(3, Total(ann, "requests"));
        Assert.Equal(1, Total(ann, "chats"));
        Assert.Equal(240, Total(ann, "tokensIn"));
        Assert.Equal(60, Total(ann, "tokensOut"));
        Assert.Equal(1, Total(ann, "errors"));
        Assert.Equal(0, Total(ann, "unreported"));
        Assert.Equal(["kagent/default-model-config"], ann.GetProperty("tokensByModel").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(["kagent/helper"], ann.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        // 240 in × 1/M + 60 out × 4/M, with the disclaimer.
        Assert.Equal(0.0005m, ann.GetProperty("totals").GetProperty("cost").GetProperty("amount").GetDecimal());
        Assert.Equal(CostDisclaimer.Text, ann.GetProperty("costDisclaimer").GetString());

        var org = await UsageAsync(_owner, "scope=org&range=7d");
        Assert.Equal(4, Total(org, "requests"));
        Assert.Equal(2, Total(org, "activeUsers"));
        Assert.Equal(["kagent/default-model-config", "unknown"], org.GetProperty("tokensByModel").EnumerateArray().Select(s => s.GetProperty("name").GetString()).Order());
        var rows = org.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("totals").GetProperty("requests").GetInt64());
        Assert.Equal(3, rows["Finance"]);
        Assert.Equal(1, rows["Not in a team"]);
    }

    [Fact]
    public async Task AUser_SeesOnlyTheirOwn_AndNeverTheDashboard()
    {
        await ChatAsync(_eve, "seed", "Hello");

        Assert.Equal(0, Total(await UsageAsync(_ann), "requests")); // eve's turn is not ann's
        foreach (var query in new[] { $"scope=user&id={_eveId}", "scope=user&id=nobody", "scope=team&id=billing", "scope=department&id=finance", "scope=org" })
            Assert.Equal(HttpStatusCode.Forbidden, (await _ann.GetAsync($"/api/analytics/usage?{query}", Ct)).StatusCode);
        Assert.Equal(["Me"], (await _ann.GetFromJsonAsync<JsonElement>("/api/analytics/scopes", Ct)).EnumerateArray().Select(n => n.GetProperty("name").GetString()));

        foreach (var path in new[] { "/api/dashboard/summary", "/api/dashboard/health", "/api/analytics/export?scope=me" })
            Assert.Equal(HttpStatusCode.Forbidden, (await _ann.GetAsync(path, Ct)).StatusCode);

        var browser = new Browser(_host);
        await browser.SubmitAsync("/Login", new() { ["Email"] = "ann@skanyxx.example", ["Password"] = Password });
        var dashboard = await browser.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.Equal("/AccessDenied", new Uri(_host.BaseAddress, dashboard.Headers.Location!).AbsolutePath);
        var analytics = await browser.GetAsync("/Analytics");
        Assert.Equal(HttpStatusCode.OK, analytics.StatusCode);
        var html = await analytics.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("href=\"/Dashboard\"", html); // not in the nav either
        Assert.Contains(CostDisclaimer.Text.Replace("'", "&#x27;"), html);
        Assert.DoesNotContain("id=\"export\"", html);
    }

    [Fact]
    public async Task ASupervisor_SeesTheirTeam_NotTheOrganization_AndTheDashboardReadOnly()
    {
        await ChatAsync(_ann, "seed", "Hello");
        await ChatAsync(_eve, "seed", "Hello");

        var team = await UsageAsync(_sue, "scope=team&id=billing");
        Assert.Equal(1, Total(team, "requests")); // ann's, not eve's
        Assert.Equal(1, Total(await UsageAsync(_sue, $"scope=user&id={_annId}"), "requests"));
        foreach (var query in new[] { $"scope=user&id={_eveId}", "scope=department&id=finance", "scope=org" })
            Assert.Equal(HttpStatusCode.Forbidden, (await _sue.GetAsync($"/api/analytics/usage?{query}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sue.GetAsync("/api/analytics/export?scope=team&id=billing", Ct)).StatusCode);

        var summary = await _sue.GetFromJsonAsync<JsonElement>("/api/dashboard/summary", Ct);
        Assert.Equal(1, summary.GetProperty("active7d").GetInt32()); // ann; eve is not in sue's team
        Assert.Equal(2, summary.GetProperty("people").GetInt32()); // sue and ann
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("topUsers").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("recentEvents").ValueKind);
        Assert.Equal(CostDisclaimer.Text, summary.GetProperty("costDisclaimer").GetString());

        var owners = await _owner.GetFromJsonAsync<JsonElement>("/api/dashboard/summary", Ct);
        Assert.Equal(2, owners.GetProperty("active7d").GetInt32());
        Assert.Equal(2, owners.GetProperty("topUsers").GetArrayLength());
        Assert.NotEmpty(owners.GetProperty("recentEvents").EnumerateArray());
        Assert.All(owners.GetProperty("recentEvents").EnumerateArray(), e =>
            Assert.Equal(["at", "action", "actor", "target"], e.EnumerateObject().Select(p => p.Name))); // no details, no address
    }

    [Fact]
    public async Task TheExport_IsTheOwners_Audited_AndHoldsNoChatText()
    {
        await ChatAsync(_ann, "helper", Question);

        var response = await _owner.GetAsync("/api/analytics/export?scope=org&range=7d", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith($"# {CostDisclaimer.Text}\n", csv);
        Assert.Contains("ann@skanyxx.example,kagent/helper,kagent/default-model-config,1,1,120,30,", csv);
        Assert.DoesNotContain("Nightjar", csv);
        Assert.DoesNotContain(_kagent.Answer, csv);

        var audit = await _owner.GetFromJsonAsync<JsonElement>("/api/identity/audit?action=analytics.exported", Ct);
        var entry = Assert.Single(audit.EnumerateArray());
        Assert.Equal("org", entry.GetProperty("details").GetProperty("scope").GetString());
    }

    [Theory]
    [InlineData("range=custom&from=2020-01-01&to=2026-01-01")] // more than a year
    [InlineData("range=custom&from=2999-01-01&to=2999-01-02")] // the future
    [InlineData("range=custom&from=2026-02-01")] // half a range
    [InlineData("range=custom&from=yesterday&to=today")]
    [InlineData("range=custom&from=0001-01-01&to=0001-01-05&compare=true")] // too far back
    [InlineData("range=1y")]
    [InlineData("scope=team")] // a team without an id
    [InlineData("scope=everyone")]
    public async Task Ranges_AreBounded_AndScopesNamed(string query)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _owner.GetAsync($"/api/analytics/usage?{query}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _owner.GetAsync($"/api/analytics/export?{query}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Health_ShowsEachDependency_WithFixedTextsOnly()
    {
        _kagent.AgentsStatus = 500; // kagent answers its agent list with a body naming its internals

        var response = await _sue.GetAsync("/api/dashboard/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(FakeKAgent.InternalDetail, body);
        Assert.DoesNotContain(fixture.ConnectionString, body);
        Assert.DoesNotContain("Password", body);
        var tiles = JsonDocument.Parse(body).RootElement.EnumerateArray()
            .ToDictionary(t => t.GetProperty("id").GetString()!, t => t.GetProperty("status").GetString());
        Assert.Equal("ok", tiles["kagent"]);
        Assert.Equal("down", tiles["agents"]);
        Assert.Equal("ok", tiles["postgres"]);
        Assert.Equal("off", tiles["dragonfly"]); // the answer cache is off in this host
        Assert.Equal("off", tiles["git"]);
        Assert.Equal("down", tiles["sandboxes"]); // enabled, but nothing listens on the test AX address
    }

    [Fact]
    public async Task TheOwnersDashboardPage_ShowsTheDisclaimer_AndLoadsChartsFromThisHost()
    {
        var browser = new Browser(_host);
        await browser.SubmitAsync("/Login", new() { ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword });

        var page = await browser.GetAsync("/Dashboard");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(Ct);
        Assert.Contains(CostDisclaimer.Text.Replace("'", "&#x27;"), html);
        Assert.Contains("/lib/chartjs/chart.umd.min.js", html);
        Assert.Contains("id=\"events\"", html);
        Assert.Equal(HttpStatusCode.OK, (await _host.Client().GetAsync("/lib/chartjs/chart.umd.min.js", Ct)).StatusCode);
    }
}
