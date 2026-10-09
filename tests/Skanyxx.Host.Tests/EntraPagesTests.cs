using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The pages around Microsoft sign-in: hidden and 404 while off (D6), settings applied to the very next challenge
/// without a restart (A3), the owner-only settings page with a write-only secret, and linking a password account (D3).
/// </summary>
public sealed class EntraPagesTests(PostgresFixture fixture, MockIdentityProvider idp) : EntraHostTestBase(fixture, idp)
{
    [Fact]
    public async Task WhileOff_TheButtonIsHidden_AndStartingASignInOrLinkIs404()
    {
        var anonymous = new Browser(Host);

        var login = await (await anonymous.GetAsync("/Login")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var start = await anonymous.SubmitAsync("/Login?handler=Microsoft", [], tokenFrom: "/Login");
        var link = await StartLinkAsync(OwnerBrowser, HostApp.OwnerPassword);
        var account = await (await OwnerBrowser.GetAsync("/Account")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Sign in with Microsoft", login);
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, link.StatusCode);
        Assert.Contains("Microsoft sign-in is not enabled", account);

        await SaveSettingsAsync();
        await SaveSettingsAsync(enabled: false, secret: null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.SubmitAsync("/Login?handler=Microsoft", [], tokenFrom: "/Login")).StatusCode);
    }

    /// <summary>A3: saving new settings changes the next challenge's authority and client, and that tenant's sign-in completes — no restart.</summary>
    [Fact]
    public async Task ASettingsChange_AppliesToTheNextChallenge_WithoutARestart()
    {
        const string secondTenant = "33333333-3333-3333-3333-333333333333";
        const string secondClient = "44444444-4444-4444-4444-444444444444";
        await SaveSettingsAsync();
        var firstChallenge = await new Browser(Host).SubmitAsync("/Login?handler=Microsoft", [], tokenFrom: "/Login");
        var first = firstChallenge.Headers.Location!;

        await SaveSettingsAsync(tenant: secondTenant, client: secondClient, secret: "second-secret");
        var browser = new Browser(Host);
        var second = await browser.SubmitAsync("/Login?handler=Microsoft", [], tokenFrom: "/Login");
        var signedIn = await EntraFlow.CompleteAsync(browser, await Flow.AtMicrosoftAsync(browser, second, EntraFlow.Claims(secondTenant, Oid(1), [Billing])));

        Assert.StartsWith($"{Idp.BaseUrl}/{Tenant}/authorize?", first.ToString());
        // The handler's defaults, kept: correlation and nonce cookies ride Entra's cross-site form_post, so they are SameSite=None and Secure.
        var cookies = firstChallenge.Headers.GetValues("Set-Cookie").Where(c => c.StartsWith(".AspNetCore.")).ToList();
        Assert.Equal(2, cookies.Count);
        Assert.All(cookies, c => Assert.Contains("secure; samesite=none; httponly", c, StringComparison.OrdinalIgnoreCase));
        var query = HttpUtility.ParseQueryString(first.Query);
        Assert.Equal(Client, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("form_post", query["response_mode"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("openid profile email", query["scope"]);
        Assert.Equal("https://skanyxx.example/signin-oidc", query["redirect_uri"]); // Identity:PublicBaseUrl, not the request
        Assert.StartsWith($"{Idp.BaseUrl}/{secondTenant}/authorize?", second.Headers.Location!.ToString());
        Assert.Equal(secondClient, HttpUtility.ParseQueryString(second.Headers.Location!.Query)["client_id"]);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(["employee"], (await MeAsync(browser)).Roles);
    }

    [Fact]
    public async Task TheSettingsPage_IsTheOwnersOnly_SavesTheMap_AndNeverShowsTheSecret()
    {
        var saved = await OwnerBrowser.SubmitAsync("/Entra", new()
        {
            ["Enabled"] = "true", ["TenantId"] = Tenant, ["ClientId"] = Client, ["ClientSecret"] = Secret,
            ["Groups[0].GroupId"] = Supervisors, ["Groups[0].Label"] = "Supervisors", ["Groups[0].Roles"] = "supervisor",
            ["Groups[1].GroupId"] = Billing, ["Groups[1].Teams"] = "billing", ["Groups[2].GroupId"] = ""
        });
        var page = await (await OwnerBrowser.GetAsync("/Entra")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var refused = await OwnerBrowser.SubmitAsync("/Entra", new()
        {
            ["Enabled"] = "true", ["TenantId"] = Tenant, ["ClientId"] = Client, ["Groups[0].GroupId"] = Billing, ["Groups[0].Roles"] = "owner"
        });
        var settings = await Owner.GetFromJsonAsync<JsonElement>("/api/identity/entra/settings", cancellationToken: TestContext.Current.CancellationToken);
        var (supervisor, _) = await PasswordMemberAsync("sam@skanyxx.example", "supervisor");
        var memberPage = await supervisor.GetAsync("/Entra");
        var memberPost = await supervisor.SubmitAsync("/Entra", new() { ["Enabled"] = "false" }, tokenFrom: "/Account");
        var memberNav = await (await supervisor.GetAsync("/Account")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var anonymous = await new Browser(Host).GetAsync("/Entra");

        var body = await saved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Saved. The next Microsoft sign-in uses these settings.", body);
        Assert.DoesNotContain(Secret, body);
        Assert.DoesNotContain(Secret, page);
        Assert.Contains("leave empty to keep it", page);
        Assert.Contains("https://skanyxx.example/signin-oidc", page);
        Assert.Contains("href=\"/Entra\"", page);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("The owner role cannot be granted", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("""[{"groupId":"00000000-0000-0000-0000-0000000000a1","label":"Supervisors","roles":["supervisor"],"teams":[]},{"groupId":"00000000-0000-0000-0000-0000000000b1","label":null,"roles":[],"teams":["billing"]}]""",
            settings.GetProperty("groups").GetRawText());
        Assert.True(settings.GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.Redirect, memberPage.StatusCode); // pages answer a refusal with the login page, as /Org does
        Assert.Equal(HttpStatusCode.Redirect, memberPost.StatusCode);
        Assert.True((await Owner.GetFromJsonAsync<JsonElement>("/api/identity/entra/settings", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("enabled").GetBoolean());
        Assert.DoesNotContain("href=\"/Entra\"", memberNav);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Login?ReturnUrl=%2FEntra", anonymous.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// D3: a signed-in password account links Microsoft from its Account page — whatever email the Microsoft account
    /// has — and becomes Entra-managed; afterwards "Sign in with Microsoft" reaches the same account.
    /// </summary>
    [Fact]
    public async Task APasswordAccount_LinksMicrosoft_AndThenSignsInWithIt()
    {
        await SaveSettingsAsync();
        var (member, memberId) = await PasswordMemberAsync("bob@skanyxx.example");
        Assert.Contains("Link Microsoft account", await (await member.GetAsync("/Account")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var challenge = await StartLinkAsync(member);
        var linked = await EntraFlow.CompleteAsync(member, await Flow.AtMicrosoftAsync(member, challenge,
            EntraFlow.Claims(Tenant, Oid(3), [Supervisors], email: "robert@contoso.example")));
        var afterLink = await MeAsync(member);
        var later = new Browser(Host);
        var signedIn = await Flow.SignInAsync(later, EntraFlow.Claims(Tenant, Oid(3), [Supervisors, Billing], email: "robert@contoso.example"));

        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Contains("Your Microsoft account is linked", await linked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["supervisor"], afterLink.Roles); // the linking browser got a fresh session with the mapped roles
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(member)).Status); // the later sign-in added a role: other sessions ended
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(memberId, (await MeAsync(later)).Id);
        Assert.Equal([("entra", $"{Tenant}|{Oid(3)}")], await LoginsAsync(memberId));
        var person = await PersonAsync("bob@skanyxx.example");
        Assert.True(person.GetProperty("entraManaged").GetBoolean());
        Assert.Equal(["billing"], await TeamsOfAsync(memberId));
    }

    /// <summary>
    /// The link result is bound to the user who started it (Identity's XSRF): a browser that started a link as one
    /// person and finishes it signed in as another links nobody.
    /// </summary>
    [Fact]
    public async Task ALinkResult_IsAcceptedOnlyForTheUserWhoStartedIt()
    {
        await SaveSettingsAsync();
        var (browser, aliceId) = await PasswordMemberAsync("alice@skanyxx.example");
        var (_, bobId) = await PasswordMemberAsync("bob@skanyxx.example");
        var challenge = await StartLinkAsync(browser);
        await browser.SubmitAsync("/Logout", [], tokenFrom: "/Account");
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SubmitAsync("/Login", new() { ["Email"] = "bob@skanyxx.example", ["Password"] = MemberPassword })).StatusCode);

        var completed = await EntraFlow.CompleteAsync(browser, await Flow.AtMicrosoftAsync(browser, challenge, EntraFlow.Claims(Tenant, Oid(4), [Supervisors])));

        Assert.Equal(HttpStatusCode.Unauthorized, completed.StatusCode);
        Assert.Contains("Microsoft sign-in did not complete", await completed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await LoginsAsync(aliceId));
        Assert.Empty(await LoginsAsync(bobId));
        Assert.Equal(bobId, (await MeAsync(browser)).Id);
    }

    private MockIdentityProvider Idp { get; } = idp;

}
