using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// D026 in the browser: the owner's People page (invite link shown once, roles, disable, revoke) and the anonymous
/// accept page, on the real host with antiforgery and the origin guard.
/// </summary>
[Collection(HostCollection.Name)]
public sealed partial class PeoplePageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string MemberEmail = "bea@skanyxx.example";
    private const string MemberPassword = "a long member passphrase";

    private HostApp _host = null!;
    private Browser _owner = null!;

    public async Task InitializeAsync()
    {
        _host = await HostApp.StartAsync(await fixture.NewDatabaseAsync());
        _owner = new Browser(_host);
        var setup = await _owner.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task InviteForm_ShowsTheLinkOnce_AndTheAcceptPageCreatesTheAccountAndSignsIn()
    {
        var invited = await InviteAsync("builder");
        var html = await invited.Content.ReadAsStringAsync();
        var token = InviteToken().Match(html).Groups[1].Value;
        var later = await (await _owner.GetAsync("/People")).Content.ReadAsStringAsync();

        var member = new Browser(_host);
        var acceptPage = await member.GetAsync("/Invite?token=" + token);
        var acceptHtml = await acceptPage.Content.ReadAsStringAsync();
        var accepted = await member.SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = MemberPassword, ["DisplayName"] = "Bea" },
            tokenFrom: "/Invite?token=" + token);
        var nav = await (await member.GetAsync("/Hooks")).Content.ReadAsStringAsync();
        var reuse = await new Browser(_host).GetAsync("/Invite?token=" + token);

        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        Assert.Matches("^skx_inv_[A-Za-z0-9_-]{43}$", token);
        Assert.Contains($"value=\"{HostApp.AllowedOrigin}/Invite?token={token}\"", html); // Identity:PublicBaseUrl, not the request
        Assert.Contains($"The link points at <strong>{new Uri(HostApp.AllowedOrigin).Authority}</strong>", html);
        Assert.True(invited.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain(token, later);
        Assert.Contains(MemberEmail, later); // pending, without its token
        Assert.Equal(HttpStatusCode.OK, acceptPage.StatusCode);
        Assert.Equal("no-referrer", acceptPage.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains(MemberEmail, acceptHtml);
        Assert.Contains("builder", acceptHtml);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/", accepted.Headers.Location?.OriginalString);
        Assert.True(member.HasCookie("skanyxx.auth"));
        Assert.Contains("Bea", nav);
        Assert.Equal(HttpStatusCode.NotFound, reuse.StatusCode);
    }

    [Fact]
    public async Task People_IsTheOwnersOnly_AndOnlyTheOwnerSeesTheNavLink()
    {
        var member = await MemberAsync("supervisor");

        var ownerNav = await (await _owner.GetAsync("/Hooks")).Content.ReadAsStringAsync();
        var ownerPage = await _owner.GetAsync("/People");
        var memberNav = await (await member.GetAsync("/Hooks")).Content.ReadAsStringAsync();
        var memberPage = await member.GetAsync("/People");
        var memberPost = await member.SubmitAsync("/People?handler=Invite", new() { ["email"] = "x@skanyxx.example", ["roles"] = "builder" },
            tokenFrom: "/Hooks");
        var anonymous = await _host.Client().GetAsync("/People");

        Assert.Contains("href=\"/People\"", ownerNav);
        Assert.Equal(HttpStatusCode.OK, ownerPage.StatusCode);
        Assert.DoesNotContain("href=\"/People\"", memberNav);
        Assert.Equal(HttpStatusCode.Redirect, memberPage.StatusCode);
        Assert.Contains("/Login", memberPage.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, memberPost.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.DoesNotContain("x@skanyxx.example", await (await _owner.GetAsync("/People")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RoleChangeAndDisable_FromThePage_TakeEffectOnTheMembersSession()
    {
        var member = await MemberAsync("builder");
        var memberId = await PersonIdAsync(MemberEmail);

        var roles = await _owner.SubmitAsync($"/People?handler=Roles&id={memberId}", new() { ["roles"] = "employee" }, tokenFrom: "/People");
        var afterRoles = await member.GetAsync("/Hooks");
        var signedInAgain = await new Browser(_host).SubmitAsync("/Login", new() { ["Email"] = MemberEmail, ["Password"] = MemberPassword });
        var me = await _host.Client(await BearerAsync()).GetFromJsonAsync<JsonElement>("/api/identity/me");
        var disable = await _owner.SubmitAsync($"/People?handler=Disable&id={memberId}", [], tokenFrom: "/People");
        var signInDisabled = await new Browser(_host).SubmitAsync("/Login", new() { ["Email"] = MemberEmail, ["Password"] = MemberPassword });

        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Contains("Roles saved.", await roles.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, afterRoles.StatusCode); // the old cookie is gone
        Assert.Equal(HttpStatusCode.Redirect, signedInAgain.StatusCode);
        Assert.Equal(["employee"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("Account disabled.", await disable.Content.ReadAsStringAsync());
        Assert.Contains("Invalid email or password.", await signInDisabled.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Revoke_FromThePage_KillsTheLink()
    {
        var token = InviteToken().Match(await (await InviteAsync("employee")).Content.ReadAsStringAsync()).Groups[1].Value;
        var invites = await _host.Client(await OwnerBearerAsync()).GetFromJsonAsync<JsonElement>("/api/identity/invites");

        var revoke = await _owner.SubmitAsync($"/People?handler=Revoke&id={invites[0].GetProperty("id").GetString()}", [], tokenFrom: "/People");
        var page = await new Browser(_host).GetAsync("/Invite?token=" + token);

        Assert.Contains("Invite revoked.", await revoke.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }

    [Fact]
    public async Task AcceptPage_UnknownOrMissingToken_Is404_AndPostNeedsAntiforgery()
    {
        var token = InviteToken().Match(await (await InviteAsync("builder")).Content.ReadAsStringAsync()).Groups[1].Value;

        var unknown = await _host.Client().GetAsync("/Invite?token=skx_inv_nope");
        var missing = await _host.Client().GetAsync("/Invite");
        var noAntiforgery = await new Browser(_host).PostFormAsync("/Invite", new() { ["Token"] = token, ["Password"] = MemberPassword });
        var stillOpen = await _host.Client().GetAsync("/Invite?token=" + token);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("invalid or has expired", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noAntiforgery.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
    }

    /// <summary>CR6 / SEC L2: every answer of the accept page carries no-store and no-referrer, failures included.</summary>
    [Fact]
    public async Task AcceptPage_FailedPosts_AreNoStoreAndNoReferrer_WithAccurateStatus()
    {
        var token = InviteToken().Match(await (await InviteAsync("builder")).Content.ReadAsStringAsync()).Groups[1].Value;
        var member = new Browser(_host);

        var tooShort = await member.SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = "short" }, tokenFrom: "/Invite?token=" + token);
        var unknown = await member.SubmitAsync("/Invite", new() { ["Token"] = "skx_inv_" + new string('C', 43), ["Password"] = MemberPassword },
            tokenFrom: "/Login");
        var accepted = await member.SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = MemberPassword }, tokenFrom: "/Invite?token=" + token);
        var spent = await new Browser(_host).SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = MemberPassword }, tokenFrom: "/Login");

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, spent.StatusCode);
        Assert.All(new[] { tooShort, unknown, accepted, spent }, response =>
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        });
    }

    /// <summary>CR3: the owner's roles are fixed, so the owner's row has no role form, and posting one anyway is refused.</summary>
    [Fact]
    public async Task OwnerRow_HasNoRoleForm_AndSavingTheOwnersRolesIsRefused()
    {
        await MemberAsync("builder");
        var ownerId = await PersonIdAsync(HostApp.OwnerEmail);
        var memberId = await PersonIdAsync(MemberEmail);

        var html = await (await _owner.GetAsync("/People")).Content.ReadAsStringAsync();
        var forced = await _owner.SubmitAsync($"/People?handler=Roles&id={ownerId}", new() { ["roles"] = "builder" }, tokenFrom: "/People");
        var stillOwner = await _owner.GetAsync("/People");

        Assert.Contains($"id={memberId}&amp;handler=Roles", html);
        Assert.DoesNotContain($"id={ownerId}&amp;handler=Roles", html);
        Assert.Contains("The owner&#x27;s roles are fixed", await forced.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, stillOwner.StatusCode); // no stamp rotation: the owner is still signed in
    }

    [Fact]
    public async Task PeoplePost_FromAForeignOrigin_Is403()
    {
        var html = await (await _owner.GetAsync("/People")).Content.ReadAsStringAsync();
        var antiforgery = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

        var response = await _owner.PostFormAsync("/People?handler=Invite",
            new() { ["email"] = "evil@skanyxx.example", ["roles"] = "builder", ["__RequestVerificationToken"] = antiforgery },
            r => r.Headers.Add("Origin", "https://evil.example"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("evil@skanyxx.example", await (await _owner.GetAsync("/People")).Content.ReadAsStringAsync());
    }

    /// <summary>Verifier round 2: an upgraded install without Identity:PublicBaseUrl starts; the invite form says what to set.</summary>
    [Fact]
    public async Task WithoutAPublicBaseUrl_TheInviteFormNamesTheSetting_AndNothingIsCreated()
    {
        await using var host = await HostApp.StartAsync(await fixture.NewDatabaseAsync(), s => s.Remove("Identity:PublicBaseUrl"));
        var owner = new Browser(host);
        await owner.SubmitAsync("/Setup", new()
        {
            ["Email"] = HostApp.OwnerEmail, ["Password"] = HostApp.OwnerPassword, ["BootstrapToken"] = HostApp.BootstrapToken
        });

        var invited = await owner.SubmitAsync("/People?handler=Invite", new() { ["email"] = MemberEmail, ["roles"] = "builder" }, tokenFrom: "/People");
        var html = await invited.Content.ReadAsStringAsync();
        var invites = await host.Client(await BearerAsync(host, HostApp.OwnerEmail, HostApp.OwnerPassword)).GetFromJsonAsync<JsonElement>("/api/identity/invites");

        Assert.Contains("Invite links need Identity:PublicBaseUrl in appsettings.json", html);
        Assert.Contains("http://localhost:5282", html);
        Assert.DoesNotContain("/Invite?token=", html);
        Assert.Equal(0, invites.GetArrayLength());
    }

    private Task<HttpResponseMessage> InviteAsync(string role) =>
        _owner.SubmitAsync("/People?handler=Invite", new() { ["email"] = MemberEmail, ["roles"] = role }, tokenFrom: "/People");

    private async Task<Browser> MemberAsync(string role)
    {
        var token = InviteToken().Match(await (await InviteAsync(role)).Content.ReadAsStringAsync()).Groups[1].Value;
        var member = new Browser(_host);
        var accepted = await member.SubmitAsync("/Invite", new() { ["Token"] = token, ["Password"] = MemberPassword }, tokenFrom: "/Invite?token=" + token);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        return member;
    }

    private async Task<string> PersonIdAsync(string email)
    {
        var people = await _host.Client(await OwnerBearerAsync()).GetFromJsonAsync<JsonElement>("/api/identity/people");
        return people.EnumerateArray().Single(p => p.GetProperty("email").GetString() == email).GetProperty("id").GetString()!;
    }

    private Task<string> OwnerBearerAsync() => BearerAsync(HostApp.OwnerEmail, HostApp.OwnerPassword);

    private Task<string> BearerAsync(string email = MemberEmail, string password = MemberPassword) => BearerAsync(_host, email, password);

    private static async Task<string> BearerAsync(HostApp host, string email, string password)
    {
        var signIn = await host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password });
        signIn.EnsureSuccessStatusCode();
        return (await signIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    [GeneratedRegex("/Invite\\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex InviteToken();
}
