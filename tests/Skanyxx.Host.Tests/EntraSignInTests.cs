using System.Net.Http.Json;
using System.Text.Json;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D027 end to end: "Sign in with Microsoft" against a mock Entra ID (form_post, PKCE, per-tenant issuer), group →
/// role/team mapping at every sign-in (D1), refusals (D2), no linking by email (D3), overage through Graph.
/// </summary>
public sealed class EntraSignInTests(PostgresFixture fixture, MockIdentityProvider idp) : EntraHostTestBase(fixture, idp)
{
    private const string Email = "bea@contoso.example";
    private const string OtherTenant = "99999999-9999-9999-9999-999999999999";

    [Fact]
    public async Task AMappedFirstSignIn_CreatesTheAccount_WithTheMappedRolesAndTeams_AndACookieSession()
    {
        await SaveSettingsAsync();
        var browser = new Browser(Host);

        var signedIn = await Flow.SignInAsync(browser, EntraFlow.Claims(Tenant, Oid(1), [Supervisors, Billing, Unmapped]), returnUrl: "/Org");

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Org", signedIn.Headers.Location!.OriginalString);
        Assert.False(browser.HasCookie("Identity.External")); // the external result is used once
        var me = await MeAsync(browser);
        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.Equal(["employee", "supervisor"], me.Roles.Order());
        var person = await PersonAsync(Email);
        Assert.Equal(me.Id, person.GetProperty("id").GetString());
        Assert.True(person.GetProperty("entraManaged").GetBoolean());
        Assert.Equal("Bea Entra", person.GetProperty("displayName").GetString());
        Assert.Equal(["billing"], await TeamsOfAsync(me.Id!));
        Assert.Equal([("entra", $"{Tenant}|{Oid(1)}")], await LoginsAsync(me.Id!)); // keyed by tid|oid, never sub or email
        Assert.Contains("managed by Entra", await (await OwnerBrowser.GetAsync("/People")).Content.ReadAsStringAsync());
        Assert.Contains("managed by Entra", await (await OwnerBrowser.GetAsync("/Org")).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("unmapped")]
    [InlineData("guest")]
    [InlineData("no groups")]
    public async Task WithoutAMappedGroup_OrAsAGuest_NoAccountIsCreated(string who)
    {
        await SaveSettingsAsync();
        var before = await PeopleCountAsync();
        var claims = who switch
        {
            "unmapped" => EntraFlow.Claims(Tenant, Oid(1), [Unmapped]),
            "guest" => EntraFlow.Claims(Tenant, Oid(1), [Supervisors], acct: 1),
            _ => EntraFlow.Claims(Tenant, Oid(1))
        };

        var refused = await Flow.SignInAsync(new Browser(Host), claims);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("has no access to Skanyxx", await refused.Content.ReadAsStringAsync());
        Assert.Equal(before, await PeopleCountAsync());
    }

    /// <summary>
    /// Wrong tenant: the handler accepts the issuer (test configuration points this tenant's authority at another
    /// issuer, whose tokens carry that issuer's tid — the mock stamps tid = issuer), and only the tid pin refuses the
    /// token. A token from a foreign issuer cannot be staged: the mock does not bind codes to the issuer that made them.
    /// </summary>
    [Fact]
    public async Task ATidThatIsNotTheConfiguredTenant_IsRefused_EvenFromAnAcceptedIssuer()
    {
        IssuerOverride = OtherTenant;
        await SaveSettingsAsync();
        var before = await PeopleCountAsync();
        var browser = new Browser(Host);

        var refused = await Flow.SignInAsync(browser, EntraFlow.Claims(OtherTenant, Oid(1), [Supervisors]));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("Microsoft sign-in did not complete", await refused.Content.ReadAsStringAsync());
        Assert.Equal(before, await PeopleCountAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(browser)).Status);
    }

    /// <summary>D1: every sign-in rewrites roles and teams from the mapping, manual edits included; a role change ends the other sessions.</summary>
    [Fact]
    public async Task EachSignIn_ReMapsRolesAndTeams_EndsOtherSessions_AndAnnouncesALostSupervisor()
    {
        await SaveSettingsAsync();
        var first = new Browser(Host);
        await Flow.SignInAsync(first, EntraFlow.Claims(Tenant, Oid(1), [Supervisors, Billing]));
        var id = (await MeAsync(first)).Id!;
        Assert.Equal(HttpStatusCode.OK, (await Owner.PutAsJsonAsync($"/api/identity/people/{id}/roles", new { roles = new[] { "builder" } })).StatusCode);
        await SaveSettingsAsync([Map(Supervisors, ["builder"], ["platform"]), Map(Billing, ["employee"], ["billing"])]);

        var second = new Browser(Host);
        var again = await Flow.SignInAsync(second, EntraFlow.Claims(Tenant, Oid(1), [Supervisors]));

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        var me = await MeAsync(second);
        Assert.Equal(id, me.Id);
        Assert.Equal(["builder"], me.Roles);
        Assert.Equal(["platform"], await TeamsOfAsync(id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(first)).Status);
        Assert.Contains(new PrivilegesRevoked(id, "roles saved without supervisor"), Revoked); // the owner's manual edit
        Assert.Equal(2, await PeopleCountAsync()); // the owner and this one account: found by its login, not created again
    }

    /// <summary>
    /// The re-map itself changes the roles (supervisor → employee), so it alone ends the earlier session (G1); every
    /// sign-in whose mapping lacks supervisor announces it (D12), a new account excepted (it has issued nothing).
    /// </summary>
    [Fact]
    public async Task LosingSupervisorAtSignIn_EndsTheEarlierSession_AndPublishesPrivilegesRevoked()
    {
        await SaveSettingsAsync();
        var first = new Browser(Host);
        await Flow.SignInAsync(first, EntraFlow.Claims(Tenant, Oid(1), [Supervisors]));
        var id = (await MeAsync(first)).Id!;
        Assert.Empty(Revoked);

        var second = new Browser(Host);
        await Flow.SignInAsync(second, EntraFlow.Claims(Tenant, Oid(1), [Billing]));

        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(first)).Status);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(second)).Status);
        Assert.Equal([new PrivilegesRevoked(id, "Entra group mapping without supervisor")], Revoked);
        Assert.Equal(["employee"], (await PersonAsync(Email)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    /// <summary>D2: a refused sign-in of an existing Entra-managed account ends its sessions and empties its roles and teams; the account stays.</summary>
    [Fact]
    public async Task ARefusedReSignIn_EndsTheSessions_AndTakesTheRolesAndTeams()
    {
        await SaveSettingsAsync();
        var first = new Browser(Host);
        await Flow.SignInAsync(first, EntraFlow.Claims(Tenant, Oid(1), [Supervisors, Billing]));
        var id = (await MeAsync(first)).Id!;

        var refused = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Unmapped]));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(first)).Status);
        var person = await PersonAsync(Email);
        Assert.Empty(person.GetProperty("roles").EnumerateArray());
        Assert.Empty(await TeamsOfAsync(id));
        Assert.Equal([new PrivilegesRevoked(id, "Microsoft sign-in refused")], Revoked);
    }

    /// <summary>
    /// Overage: no groups claim, <c>_claim_names</c> says they are elsewhere. Graph is asked — app-only token with the
    /// same client — about the mapped ids only, in batches of 20; the token's <c>_claim_sources</c> endpoint is never called.
    /// </summary>
    [Fact]
    public async Task Overage_IsResolvedWithGraph_AskingOnlyTheMappedGroups()
    {
        var filler = Enumerable.Range(1, 24).Select(i => $"00000000-0000-0000-0001-{i:D12}").ToArray();
        await SaveSettingsAsync([Map(Supervisors, ["supervisor"]), .. filler.Select(g => Map(g, ["employee"]))]);
        Graph.MemberOf.UnionWith([Supervisors, Unmapped]);
        var claims = EntraFlow.Claims(Tenant, Oid(1));
        claims["_claim_names"] = new { groups = "src1" };
        claims["_claim_sources"] = new { src1 = new { endpoint = "https://graph.windows.net/tenant/users/x/getMemberObjects" } };
        var browser = new Browser(Host);

        var signedIn = await Flow.SignInAsync(browser, claims);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(["supervisor"], (await MeAsync(browser)).Roles);
        Assert.Equal([20, 5], Graph.CheckBatches.Select(b => b.Length));
        Assert.Equal([Supervisors, .. filler], Graph.CheckBatches.SelectMany(b => b).Order());
        Assert.All(Graph.Requests.Where(r => r.Uri.Host == "graph.microsoft.com"),
            r => Assert.Equal($"https://graph.microsoft.com/v1.0/users/{Oid(1)}/checkMemberGroups", r.Uri.ToString()));
        var token = Assert.Single(Graph.TokenRequests);
        Assert.Equal($"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token", token.Uri.ToString());
        Assert.Contains($"client_id={Client}", token.Body);
        Assert.Contains("grant_type=client_credentials", token.Body);
        Assert.DoesNotContain(Graph.Requests, r => r.Uri.Host == "graph.windows.net");
    }

    [Fact]
    public async Task Overage_WhenGraphFails_Is502_AndNothingChanges()
    {
        await SaveSettingsAsync();
        Graph.CheckStatus = HttpStatusCode.ServiceUnavailable;
        var before = await PeopleCountAsync();
        var claims = EntraFlow.Claims(Tenant, Oid(1));
        claims["_claim_names"] = new { groups = "src1" };

        var failed = await Flow.SignInAsync(new Browser(Host), claims);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("could not read your groups", await failed.Content.ReadAsStringAsync());
        Assert.Equal(before, await PeopleCountAsync());
    }

    /// <summary>D3 / nOAuth: the same email on a password account is never a match; the account stays unlinked.</summary>
    [Fact]
    public async Task AnEmailThatBelongsToAPasswordAccount_IsNotLinkedByEmail()
    {
        await SaveSettingsAsync();
        var (_, memberId) = await PasswordMemberAsync(Email);

        var refused = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors], email: Email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Sign in with your password, then link your Microsoft account", await refused.Content.ReadAsStringAsync());
        Assert.Empty(await LoginsAsync(memberId));
        Assert.Equal(["builder"], (await PersonAsync(Email)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task ADisabledAccount_IsRefused()
    {
        await SaveSettingsAsync();
        var browser = new Browser(Host);
        await Flow.SignInAsync(browser, EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var id = (await MeAsync(browser)).Id!;
        Assert.Equal(HttpStatusCode.OK, (await Owner.PostAsync($"/api/identity/people/{id}/disable", null)).StatusCode);

        var refused = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.True((await PersonAsync(Email)).GetProperty("disabled").GetBoolean());
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    public async Task AReturnUrlOffThisSite_EndsAtHome(string returnUrl)
    {
        await SaveSettingsAsync();

        var signedIn = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]), returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/", signedIn.Headers.Location!.OriginalString);
    }

    /// <summary>The completion checks the return URL itself too, not only the challenge that sanitised it.</summary>
    [Fact]
    public async Task AReturnUrlOffThisSite_IsRefusedAtTheCompletionAsWell()
    {
        await SaveSettingsAsync();
        var browser = new Browser(Host);
        var challenge = await browser.SubmitAsync("/Login?handler=Microsoft", [], tokenFrom: "/Login");
        var callback = await Flow.AtMicrosoftAsync(browser, challenge, EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        var signedIn = await browser.GetAsync("/Login?handler=Microsoft&returnUrl=" + Uri.EscapeDataString("https://evil.example/steal"));

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/", signedIn.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// D11: the owner cannot link Microsoft (the page says so and the challenge is 403), and a Microsoft login found on the
    /// owner anyway (written around the link) signs nobody in and changes nothing — neither a grant nor a refusal.
    /// </summary>
    [Fact]
    public async Task TheOwner_CannotLinkMicrosoft_AndIsNeverReachedThroughIt()
    {
        await SaveSettingsAsync();
        var ownerId = (await MeAsync(OwnerBrowser)).Id!;

        var page = await (await OwnerBrowser.GetAsync("/Account")).Content.ReadAsStringAsync();
        var link = await StartLinkAsync(OwnerBrowser, HostApp.OwnerPassword);
        await InsertLoginAsync(ownerId, $"{Tenant}|{Oid(7)}");
        var granted = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(7), [Supervisors, Billing]));
        var refused = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(7), [Unmapped]));
        var password = await Host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email = HostApp.OwnerEmail, password = HostApp.OwnerPassword });

        Assert.Contains("The owner signs in with a password only", page);
        Assert.Equal(HttpStatusCode.OK, password.StatusCode); // D8 never applies to the owner, Microsoft login or not
        Assert.DoesNotContain("Link Microsoft account", page);
        Assert.Equal(HttpStatusCode.Forbidden, link.StatusCode);
        Assert.Contains("The owner signs in with a password only", await link.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, granted.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(["owner"], (await MeAsync(OwnerBrowser)).Roles); // this session survived both
        Assert.Empty(await TeamsOfAsync(ownerId));
        Assert.Empty(Revoked);
    }
}
