using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Slice 2 in the browser: the Library page over the memory module, on the real host. Department finance holds teams
/// billing (mia) and sales (otto). The owner is a supervisor in no team. Every rule is memory's AccessPolicy; the page
/// only shows what it answers.
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class LibraryPageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Password = "a long member passphrase";

    private HostApp _host = null!;
    private HttpClient _ownerApi = null!;
    private HttpClient _miaApi = null!;
    private Browser _mia = null!;
    private string _miaId = null!;
    private string _database = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await fixture.NewDatabaseAsync();
        _host = await HostApp.StartAsync(_database);
        _ownerApi = await _host.OwnerAsync();
        (_miaApi, _miaId) = await PersonAsync("mia@skanyxx.example", "employee");
        var (ottoApi, ottoId) = await PersonAsync("otto@skanyxx.example", "employee");
        Created(await _ownerApi.PostAsJsonAsync("/api/identity/org/departments", new { slug = "finance", name = "Finance" }));
        Created(await _ownerApi.PostAsJsonAsync("/api/identity/org/teams", new { slug = "billing", name = "Billing", department = "finance" }));
        Created(await _ownerApi.PostAsJsonAsync("/api/identity/org/teams", new { slug = "sales", name = "Sales", department = "finance" }));
        (await _ownerApi.PutAsync($"/api/identity/org/teams/billing/members/{_miaId}", null)).EnsureSuccessStatusCode();
        (await _ownerApi.PutAsync($"/api/identity/org/teams/sales/members/{ottoId}", null)).EnsureSuccessStatusCode();

        Created(await PutCardAsync(_ownerApi, "company", "refund-window", "Refunds within 14 days"));
        Created(await PutCardAsync(_miaApi, "team:billing", "billing-close", "Billing closes on the 25th"));
        Created(await PutCardAsync(_miaApi, $"personal:{_miaId}", "mia-note", "Mia invoices on Fridays"));
        Created(await PutCardAsync(ottoApi, "team:sales", "sales-secret", "Sales discount ceiling is 30%"));

        _mia = await SignInAsync("mia@skanyxx.example", Password);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task AnEmployee_SeesCompanyTheirTeamAndTheirOwn_NotAnotherTeams()
    {
        var page = await _mia.GetAsync("/Library");
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var otherTeamCard = await _mia.GetAsync("/Library?scope=team:sales&key=sales-secret");
        var otherTeamFilter = await _mia.GetAsync("/Library?filter=team:sales");
        var searched = await (await _mia.GetAsync("/Library?q=refund")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"/Library\"", html); // in everyone's nav
        Assert.Contains("Refunds within 14 days", html);
        Assert.Contains("Billing closes on the 25th", html);
        Assert.Contains("Mia invoices on Fridays", html);
        Assert.Contains("personal (you)", html);
        Assert.DoesNotContain("team:sales", html);
        Assert.DoesNotContain("discount ceiling", html);
        Assert.Equal(HttpStatusCode.Forbidden, otherTeamCard.StatusCode);
        Assert.DoesNotContain("discount ceiling", await otherTeamCard.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, otherTeamFilter.StatusCode);
        Assert.DoesNotContain("discount ceiling", await otherTeamFilter.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Refunds within 14 days", searched);
        Assert.DoesNotContain("Billing closes", searched);
    }

    [Fact]
    public async Task LiftButtons_AreExactlyTheTargetsThePersonMayWrite()
    {
        var personal = await HtmlAsync(_mia, $"/Library?scope=personal:{_miaId}&key=mia-note");
        var team = await HtmlAsync(_mia, "/Library?scope=team:billing&key=billing-close");
        var company = await HtmlAsync(_mia, "/Library?scope=company&key=refund-window");
        var owner = await SignInAsync(HostApp.OwnerEmail, HostApp.OwnerPassword);
        var ownerOnTeam = await HtmlAsync(owner, "/Library?scope=team:billing&key=billing-close");
        var ownerOnCompany = await HtmlAsync(owner, "/Library?scope=company&key=refund-window");

        Assert.Equal(["team:billing", "department:finance"], LiftButtons(personal));
        Assert.Equal(["department:finance"], LiftButtons(team));
        Assert.Empty(LiftButtons(company));
        Assert.Equal(["company"], LiftButtons(ownerOnTeam)); // oversight: a supervisor lifts a team card to company
        Assert.Empty(LiftButtons(ownerOnCompany));
        Assert.Contains("data-rename=\"mia-note\"", personal);
        Assert.Contains("data-rename=\"billing-close\"", team);
        Assert.DoesNotContain("data-rename", company);
        Assert.DoesNotContain("data-rename", ownerOnTeam); // reads every team, writes none it is not in
        Assert.Contains("data-rename", ownerOnCompany);
    }

    [Fact]
    public async Task LiftAndRename_ThroughThePage()
    {
        var lifted = await _mia.SubmitAsync($"/Library?handler=Lift&scope=personal:{_miaId}&key=mia-note",
            new() { ["target"] = "team:billing" }, tokenFrom: "/Library");
        var renamed = await _mia.SubmitAsync("/Library?handler=Rename&scope=team:billing&key=mia-note",
            new() { ["newKey"] = "invoice-day", ["version"] = "1" }, tokenFrom: "/Library");
        var stale = await _mia.SubmitAsync("/Library?handler=Rename&scope=team:billing&key=invoice-day",
            new() { ["newKey"] = "invoice-days", ["version"] = "1" }, tokenFrom: "/Library");
        var taken = await _mia.SubmitAsync("/Library?handler=Rename&scope=team:billing&key=invoice-day",
            new() { ["newKey"] = "billing-close", ["version"] = "2" }, tokenFrom: "/Library");
        var toCompany = await _mia.SubmitAsync($"/Library?handler=Lift&scope=personal:{_miaId}&key=mia-note",
            new() { ["target"] = "company" }, tokenFrom: "/Library");
        var badKey = await _mia.SubmitAsync("/Library?handler=Rename&scope=team:billing&key=invoice-day",
            new() { ["newKey"] = "Bad Key", ["version"] = "2" }, tokenFrom: "/Library");
        var copy = await _miaApi.GetFromJsonAsync<JsonElement>("/api/memory/cards/team:billing/invoice-day", cancellationToken: TestContext.Current.CancellationToken);
        var original = await _miaApi.GetFromJsonAsync<JsonElement>($"/api/memory/cards/personal:{_miaId}/mia-note", cancellationToken: TestContext.Current.CancellationToken);
        var companyCopy = await _ownerApi.GetAsync("/api/memory/cards/company/mia-note", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, lifted.StatusCode); // post/redirect/get
        Assert.Equal("/Library?scope=team%3Abilling&key=mia-note#card", lifted.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, renamed.StatusCode);
        Assert.Equal("/Library?scope=team%3Abilling&key=invoice-day#card", renamed.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Stale version 1", await stale.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("already exists", await taken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, toCompany.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        Assert.Equal(2, copy.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, original.GetProperty("liftedFromId").ValueKind);
        Assert.True(copy.GetProperty("liftedFromId").GetInt64() > 0);
        Assert.Equal(HttpStatusCode.NotFound, companyCopy.StatusCode);
        Assert.Contains($"Lifted from <code>personal:{_miaId}/mia-note</code>", await HtmlAsync(_mia, "/Library?scope=team:billing&key=invoice-day"));
    }

    /// <summary>CR L3: a success redirects to its card and says what it did once; a refresh re-sends nothing.</summary>
    [Fact]
    public async Task ASuccessfulAction_RedirectsToItsCard_AndTheMessageShowsOnce()
    {
        var lifted = await _mia.SubmitAsync($"/Library?handler=Lift&scope=personal:{_miaId}&key=mia-note&q=invoices",
            new() { ["target"] = "team:billing" }, tokenFrom: "/Library");
        var landed = await HtmlAsync(_mia, lifted.Headers.Location!.OriginalString);
        var refreshed = await HtmlAsync(_mia, lifted.Headers.Location!.OriginalString);
        var renamed = await _mia.SubmitAsync("/Library?handler=Rename&scope=team:billing&key=mia-note",
            new() { ["newKey"] = "invoice-day", ["version"] = "1" }, tokenFrom: "/Library");
        var afterRename = await HtmlAsync(_mia, renamed.Headers.Location!.OriginalString);

        Assert.Equal("/Library?scope=team%3Abilling&key=mia-note&q=invoices#card", lifted.Headers.Location!.OriginalString);
        Assert.Contains("Lifted to team:billing. The original stays where it was.", landed);
        Assert.Contains("data-rename=\"mia-note\"", landed); // the copy is open
        Assert.DoesNotContain("Lifted to", refreshed);
        Assert.Contains("Renamed to invoice-day.", afterRename);
        Assert.Contains("data-rename=\"invoice-day\"", afterRename);
    }

    /// <summary>CR L6: when the action and then the list both fail, the page reports the action's refusal.</summary>
    [Fact]
    public async Task TheFirstRefusal_IsTheOneReported()
    {
        var response = await _mia.SubmitAsync("/Library?handler=Rename&scope=team:billing&key=billing-close&filter=team:sales",
            new() { ["newKey"] = "billing-closing", ["version"] = "9" }, tokenFrom: "/Library");
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Stale version 9", html);
        Assert.DoesNotContain("not readable", html);
    }

    /// <summary>The inputs carry memory's own limits (Core's CardFormat), not copies of them.</summary>
    [Fact]
    public async Task Inputs_RenderMemorysLimits()
    {
        var html = await HtmlAsync(_mia, "/Library?scope=team:billing&key=billing-close");

        Assert.Contains("maxlength=\"500\" aria-label=\"Search\"", html);
        Assert.Contains("maxlength=\"80\" pattern=\"[a-z0-9][a-z0-9\\-]{0,79}\"", html);
    }

    /// <summary>CR L5: card text is shown as text — no markup, no link built from it.</summary>
    [Fact]
    public async Task CardText_IsEncoded_AndNeverALink()
    {
        Created(await _miaApi.PutAsJsonAsync("/api/memory/cards/team:billing/xss-probe", new
        {
            version = 0, type = "decision", what = "<script>alert('what')</script>", why = "javascript:alert('why')",
            body = "<img src=x onerror=alert('body')>", source = "javascript:alert('source')"
        }, cancellationToken: TestContext.Current.CancellationToken));

        var list = await HtmlAsync(_mia, "/Library");
        var card = await HtmlAsync(_mia, "/Library?scope=team:billing&key=xss-probe");

        foreach (var html in new[] { list, card })
        {
            Assert.DoesNotContain("<script>alert", html);
            Assert.Contains("&lt;script&gt;alert(&#x27;what&#x27;)&lt;/script&gt;", html);
            Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("<img src=x", card);
        Assert.Contains("&lt;img src=x onerror=alert(&#x27;body&#x27;)&gt;", card);
        Assert.Contains("<code>javascript:alert(&#x27;source&#x27;)</code>", card);
        Assert.Contains("javascript:alert(&#x27;why&#x27;)", card);
    }

    /// <summary>D103: a company draft opens for supervisors and its author only; to anyone else it is a missing card.</summary>
    [Fact]
    public async Task ACompanyDraft_IsAMissingCard_ToAnEmployee()
    {
        Created(await PutCardAsync(_ownerApi, "company", "draft-policy", "Draft refund policy"));
        await using (var db = new NpgsqlConnection(_database))
        {
            await db.OpenAsync(TestContext.Current.CancellationToken);
            await using var draft = new NpgsqlCommand("UPDATE memory_cards SET status = 'candidate' WHERE key = 'draft-policy'", db);
            Assert.Equal(1, await draft.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        var hidden = await _mia.GetAsync("/Library?scope=company&key=draft-policy");
        var missing = await _mia.GetAsync("/Library?scope=company&key=no-such-card");
        var owner = await SignInAsync(HostApp.OwnerEmail, HostApp.OwnerPassword);
        var ownerView = await owner.GetAsync("/Library?scope=company&key=draft-policy");

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.DoesNotContain("Draft refund policy", await hidden.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
        Assert.Contains("Draft refund policy", await ownerView.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Posts_NeedAntiforgery_AndTheOwnOrigin_AndASignIn()
    {
        var token = Antiforgery().Match(await HtmlAsync(_mia, "/Library")).Groups[1].Value;
        var path = $"/Library?handler=Lift&scope=personal:{_miaId}&key=mia-note";

        var noToken = await _mia.PostFormAsync(path, new() { ["target"] = "team:billing" });
        var foreign = await _mia.PostFormAsync(path, new() { ["target"] = "team:billing", ["__RequestVerificationToken"] = token },
            r => r.Headers.Add("Origin", "https://evil.example"));
        var anonymousGet = await _host.Client().GetAsync("/Library", TestContext.Current.CancellationToken);
        var anonymousPost = await _host.Client().PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["target"] = "team:billing" }), TestContext.Current.CancellationToken);
        var teamCard = await _miaApi.GetAsync("/api/memory/cards/team:billing/mia-note", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymousGet.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymousPost.StatusCode); // the fallback policy, before antiforgery
        Assert.Contains("/Login", anonymousPost.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, teamCard.StatusCode); // nothing was lifted
    }

    private static List<string> LiftButtons(string html) => LiftButton().Matches(html).Select(m => m.Groups[1].Value).ToList();

    private static async Task<string> HtmlAsync(Browser browser, string path) => await (await browser.GetAsync(path)).Content.ReadAsStringAsync();

    private static void Created(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.Created, response.StatusCode);

    private static Task<HttpResponseMessage> PutCardAsync(HttpClient client, string scope, string key, string what) =>
        client.PutAsJsonAsync($"/api/memory/cards/{scope}/{key}", new { version = 0, type = "decision", what, why = "Finance policy" });

    private async Task<Browser> SignInAsync(string email, string password)
    {
        var browser = new Browser(_host);
        var signedIn = await browser.SubmitAsync("/Login", new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return browser;
    }

    private async Task<(HttpClient Client, string Id)> PersonAsync(string email, string role)
    {
        var invite = await _ownerApi.PostAsJsonAsync("/api/identity/invites", new { email, roles = new[] { role } });
        invite.EnsureSuccessStatusCode();
        var link = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("link").GetString()!;
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var accepted = await _host.Client().PostAsJsonAsync("/api/identity/invites/accept", new { token, password = Password });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        return (_host.Client(body.GetProperty("tokens").GetProperty("accessToken").GetString()!), body.GetProperty("user").GetProperty("id").GetString()!);
    }

    [GeneratedRegex("data-lift=\"([^\"]+)\"")]
    private static partial Regex LiftButton();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();
}
