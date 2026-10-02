using System.Net.Http.Json;
using System.Text.Json;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Host.Tests;

/// <summary>
/// QA rounds 1 and 2 of slice 4, end to end: a linked account signs in with Microsoft only while it is on, answered like
/// a wrong password (D8, SEC N3), and turning it on ends the sessions opened while it was off (D15); guests and tokens
/// without <c>acct</c> stay out (D9); linking needs the password and the owner can undo it, except for an account whose
/// only sign-in it is (D10, D16); the owner stays local (D11); a revocation is published only when owed, and a failed
/// one is retried by the next sign-in (D13); plus the IdP's own refusal, unverified email domains, display-name
/// truncation, unreadable Graph answers, the backchannel and the key-ring warning.
/// </summary>
public sealed class EntraAccountSafetyTests(PostgresFixture fixture, MockIdentityProvider idp) : EntraHostTestBase(fixture, idp)
{
    private const string Bob = "bob@skanyxx.example";
    private const string OtherTenant = "99999999-9999-9999-9999-999999999999";

    /// <summary>
    /// D8: linking ends the account's earlier password sessions (even with its roles unchanged); password and API
    /// sign-in are then refused while Microsoft sign-in is on — with the wrong-password answer, right password or not
    /// (SEC N3) — and work again once it is off. Turning it back on ends the sessions opened meanwhile (D15).
    /// </summary>
    [Fact]
    public async Task ALinkedAccount_SignsInWithMicrosoftOnly_WhileItIsOn_AndWithItsPasswordOnceItIsOff()
    {
        await SaveSettingsAsync();
        var (member, _) = await PasswordMemberAsync(Bob, role: "employee");
        var older = new Browser(Host);
        Assert.Equal(HttpStatusCode.Redirect, (await older.SubmitAsync("/Login", new() { ["Email"] = Bob, ["Password"] = MemberPassword })).StatusCode);
        await LinkAsync(member, Oid(3), [Billing]); // employee → employee: no role change
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(older)).Status);
        var (other, _) = await PasswordMemberAsync("carol@skanyxx.example");

        var form = await new Browser(Host).SubmitAsync("/Login", new() { ["Email"] = Bob, ["Password"] = MemberPassword });
        var api = await PasswordSignInAsync(Bob, MemberPassword);
        var wrong = await PasswordSignInAsync(Bob, "not the password at all");
        await SaveSettingsAsync(enabled: false, secret: null);
        var whileOff = await PasswordSignInAsync(Bob, MemberPassword);
        var offBrowser = new Browser(Host);
        Assert.Equal(HttpStatusCode.Redirect, (await offBrowser.SubmitAsync("/Login", new() { ["Email"] = Bob, ["Password"] = MemberPassword })).StatusCode);
        var refreshToken = (await whileOff.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("refreshToken").GetString();
        await SaveSettingsAsync();
        var refreshAfterOn = await Host.Client().PostAsJsonAsync("/api/identity/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Contains("Invalid email or password.", await form.Content.ReadAsStringAsync());
        Assert.DoesNotContain(EntraPasswordMessage, await form.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
            (await api.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()); // no oracle
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(other)).Status); // an unlinked account signed in with its password while on
        Assert.Equal(HttpStatusCode.OK, whileOff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(offBrowser)).Status); // D15: turning it on ended the cookie
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterOn.StatusCode); // ... and the refresh chain
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(other)).Status); // an unlinked account is left alone
    }

    /// <summary>
    /// D8 on refresh, for a token that outlived every rule (a login written around the app): refused while on (403,
    /// not consumed) and honoured once off.
    /// </summary>
    [Fact]
    public async Task ARefreshToken_OfAManagedAccount_IsRefusedWhileOn_AndHonouredOnceOff()
    {
        await SaveSettingsAsync();
        var (_, bobId) = await PasswordMemberAsync(Bob, role: "employee");
        var signedIn = await PasswordSignInAsync(Bob, MemberPassword);
        var refreshToken = (await signedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("refreshToken").GetString();
        await InsertLoginAsync(bobId, KeyOf(Oid(4)));

        var whileOn = await Host.Client().PostAsJsonAsync("/api/identity/refresh", new { refreshToken });
        await SaveSettingsAsync(enabled: false, secret: null);
        var whileOff = await Host.Client().PostAsJsonAsync("/api/identity/refresh", new { refreshToken });

        Assert.Equal(HttpStatusCode.Forbidden, whileOn.StatusCode);
        Assert.Contains(EntraPasswordMessage, await whileOn.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, whileOff.StatusCode);
    }

    /// <summary>SEC N3: while Microsoft sign-in is on the Login page points everyone at it, the same for every visitor.</summary>
    [Fact]
    public async Task TheLoginPage_PointsAtTheMicrosoftButton_OnlyWhileItIsOn()
    {
        var off = await (await new Browser(Host).GetAsync("/Login")).Content.ReadAsStringAsync();
        await SaveSettingsAsync();
        var on = await (await new Browser(Host).GetAsync("/Login")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(MicrosoftHint, off);
        Assert.Contains(MicrosoftHint, on);
    }

    /// <summary>D9: no <c>acct</c> claim cannot tell a guest from a member: refused with the owner's fix, and an existing account is left alone.</summary>
    [Fact]
    public async Task ATokenWithoutAcct_IsRefused_NamingTheMissingClaim_AndChangesNothing()
    {
        await SaveSettingsAsync();
        var first = new Browser(Host);
        await Flow.SignInAsync(first, EntraFlow.Claims(Tenant, Oid(1), [Supervisors, Billing]));
        var before = await PeopleCountAsync();

        var existing = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors], acct: null));
        var newcomer = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(2), [Supervisors], email: "new@contoso.example", acct: null));

        Assert.Equal(HttpStatusCode.Forbidden, existing.StatusCode);
        Assert.Contains("optional claim &#x27;acct&#x27;", await existing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, newcomer.StatusCode);
        Assert.Equal(before, await PeopleCountAsync());
        Assert.Equal(["employee", "supervisor"], (await MeAsync(first)).Roles.Order()); // still signed in, roles kept
        Assert.Empty(Revoked);
    }

    /// <summary>D9: an <c>idp</c> naming another tenant is a guest whatever <c>acct</c> says; one naming this tenant is not.</summary>
    [Fact]
    public async Task AnIdpFromAnotherTenant_IsAGuest_EvenWithAcctZero()
    {
        await SaveSettingsAsync();
        var guest = EntraFlow.Claims(Tenant, Oid(1), [Supervisors]);
        guest["idp"] = $"https://sts.windows.net/{OtherTenant}/";
        var member = EntraFlow.Claims(Tenant, Oid(2), [Billing], email: "member@contoso.example");
        member["idp"] = $"https://sts.windows.net/{Tenant}/";
        var before = await PeopleCountAsync();

        var refused = await Flow.SignInAsync(new Browser(Host), guest);
        var admitted = await Flow.SignInAsync(new Browser(Host), member);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("has no access to Skanyxx", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, admitted.StatusCode);
        Assert.Equal(before + 1, await PeopleCountAsync());
    }

    /// <summary>
    /// D10: a session cookie alone (stolen, or a browser left open) cannot start a link — the current password is
    /// required, wrong ones count toward the lockout, and a locked-out account cannot link even with the right one.
    /// </summary>
    [Fact]
    public async Task Linking_NeedsTheCurrentPassword_UnderTheLockoutRules()
    {
        await SaveSettingsAsync();
        var (stolen, memberId) = await PasswordMemberAsync(Bob);

        var none = await stolen.SubmitAsync("/Account?handler=LinkMicrosoft", [], tokenFrom: "/Account");
        var wrong = await StartLinkAsync(stolen, "a guess at the password");
        var answers = new List<string>();
        for (var i = 0; i < 5; i++)
            answers.Add(await (await StartLinkAsync(stolen, "another guess")).Content.ReadAsStringAsync());
        var lockedOut = await StartLinkAsync(stolen);

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Contains("Enter your current password", await none.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Contains("not your current password", await wrong.Content.ReadAsStringAsync());
        Assert.Contains(answers, a => a.Contains("Too many wrong passwords"));
        Assert.Equal(HttpStatusCode.Forbidden, lockedOut.StatusCode);
        Assert.Contains("Too many wrong passwords", await lockedOut.Content.ReadAsStringAsync());
        Assert.All([none, wrong, lockedOut], r => Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode)); // never sent to Microsoft
        Assert.Empty(await LoginsAsync(memberId));
    }

    [Fact]
    public async Task WithTheRightPassword_TheLinkGoesToMicrosoft()
    {
        await SaveSettingsAsync();
        var (member, _) = await PasswordMemberAsync(Bob);

        var challenge = await StartLinkAsync(member);

        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.StartsWith($"{Idp.BaseUrl}/{Tenant}/authorize?", challenge.Headers.Location!.ToString());
    }

    /// <summary>D10: the owner removes a Microsoft login — audited, sessions end, the account is local-only and its password works again.</summary>
    [Fact]
    public async Task TheOwner_RemovesAMicrosoftLogin_TheAccountIsLocalOnly_AndItsSessionsEnd()
    {
        await SaveSettingsAsync();
        var (member, memberId) = await PasswordMemberAsync(Bob);
        await LinkAsync(member, Oid(5), [Billing]);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(member)).Status);

        var removed = await OwnerBrowser.SubmitAsync($"/People?handler=RemoveMicrosoft&id={memberId}", [], tokenFrom: "/People");
        var again = await Owner.DeleteAsync($"/api/identity/people/{memberId}/entra-login");
        var password = await PasswordSignInAsync(Bob, MemberPassword);
        var bearer = (await password.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokens").GetProperty("accessToken").GetString();
        var byMember = await Host.Client(bearer).DeleteAsync($"/api/identity/people/{memberId}/entra-login");
        var microsoft = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(5), [Billing], email: Bob));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Contains("Microsoft login removed", await removed.Content.ReadAsStringAsync());
        Assert.Empty(await LoginsAsync(memberId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(member)).Status);
        Assert.False((await PersonAsync(Bob)).GetProperty("entraManaged").GetBoolean());
        Assert.Equal(["employee"], (await PersonAsync(Bob)).GetProperty("roles").EnumerateArray().Select(r => r.GetString())); // kept, now edited by hand
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, microsoft.StatusCode); // the Microsoft identity no longer reaches the account
    }

    /// <summary>
    /// D16: an account Microsoft sign-in created has no password, so its Microsoft login is its only sign-in: removal is
    /// 409 and the login stays; People offers the button (with a confirmation) only for accounts with a password.
    /// </summary>
    [Fact]
    public async Task TheOnlySignInOfAnAccount_CannotBeRemoved_AndPeopleOffersNoButtonForIt()
    {
        await SaveSettingsAsync();
        var microsoft = new Browser(Host);
        await Flow.SignInAsync(microsoft, EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var createdId = (await PersonAsync(Email)).GetProperty("id").GetString()!;
        var (member, memberId) = await PasswordMemberAsync(Bob);
        await LinkAsync(member, Oid(5), [Billing]);

        var api = await Owner.DeleteAsync($"/api/identity/people/{createdId}/entra-login");
        var form = await OwnerBrowser.SubmitAsync($"/People?handler=RemoveMicrosoft&id={createdId}", [], tokenFrom: "/People");
        var page = await (await OwnerBrowser.GetAsync("/People")).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, api.StatusCode);
        Assert.Contains("Microsoft is this account", await api.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, form.StatusCode);
        Assert.Single(await LoginsAsync(createdId));
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(microsoft)).Status); // nothing ended
        Assert.False((await PersonAsync(Email)).GetProperty("hasPassword").GetBoolean());
        Assert.False(OffersRemoval(page, createdId));
        Assert.True(OffersRemoval(page, memberId));
        Assert.Contains("return confirm(", page);
    }

    /// <summary>
    /// Linking an account that is already linked, or one without a password, is refused before the password step-up, so
    /// no attempt counts toward the lockout.
    /// </summary>
    [Fact]
    public async Task LinkingAnAlreadyLinkedAccount_IsRefusedBeforeThePasswordIsChecked()
    {
        await SaveSettingsAsync();
        var microsoft = new Browser(Host);
        await Flow.SignInAsync(microsoft, EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var id = (await PersonAsync(Email)).GetProperty("id").GetString()!;

        var answers = new List<HttpResponseMessage>();
        for (var i = 0; i < 6; i++)
            answers.Add(await StartLinkAsync(microsoft, "a guess"));

        await ScalarAsync("""DELETE FROM identity_user_logins WHERE "UserId" = @id""", id); // around the rules: no login, no password
        var passwordless = await StartLinkAsync(microsoft, "a guess");

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.Conflict, a.StatusCode));
        Assert.Contains("already linked", await answers[0].Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, passwordless.StatusCode);
        Assert.Contains("has no password", await passwordless.Content.ReadAsStringAsync());
        Assert.Equal(0, await FailedCountAsync(id));
    }

    /// <summary>
    /// D13: losing supervisor at sign-in with the revocation failing gives no session (500, the re-map stays committed)
    /// and leaves it owed; the next sign-in publishes it and settles the debt, and the one after publishes nothing.
    /// </summary>
    [Fact]
    public async Task AFailedRevocationAtSignIn_Is500_StaysOwed_AndTheNextSignInPublishesAndSettlesIt()
    {
        await SaveSettingsAsync();
        await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors]));
        var id = (await PersonAsync(Email)).GetProperty("id").GetString()!;
        FailRevocations = true;

        var failedBrowser = new Browser(Host);
        var failed = await Flow.SignInAsync(failedBrowser, EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var rolesAfterFailure = (await PersonAsync(Email)).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();
        var owedAfterFailure = await OwedAsync(id);
        FailRevocations = false;
        var retried = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var owedAfterRetry = await OwedAsync(id);
        var quiet = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(failedBrowser)).Status);
        Assert.Equal(["employee"], rolesAfterFailure);
        Assert.True(owedAfterFailure);
        Assert.Equal(HttpStatusCode.Redirect, retried.StatusCode);
        Assert.False(owedAfterRetry);
        Assert.Equal(HttpStatusCode.Redirect, quiet.StatusCode);
        Assert.Equal([new PrivilegesRevoked(id, "Entra group mapping without supervisor"), new PrivilegesRevoked(id, "an earlier revocation retried")], Revoked);
    }

    /// <summary>D13: a Microsoft sign-in that takes nothing away (and owes nothing) does not touch the memory database, so its outage costs nobody their sign-in.</summary>
    [Fact]
    public async Task AMicrosoftSignIn_ThatTakesNothingAway_WorksWhileTheMemoryDatabaseIsDown()
    {
        await SaveSettingsAsync();
        FailRevocations = true;

        var created = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var again = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));
        var promoted = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors, Billing]));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, promoted.StatusCode);
        Assert.Empty(Revoked);
    }

    /// <summary>D13 with D089: supervisor taken away on People while revoking fails stays owed, and the account's next Microsoft sign-in publishes it.</summary>
    [Fact]
    public async Task APeopleRevocationThatFailed_IsPublishedByTheNextMicrosoftSignIn()
    {
        await SaveSettingsAsync();
        await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors]));
        var id = (await PersonAsync(Email)).GetProperty("id").GetString()!;
        FailRevocations = true;

        var save = await Owner.PutAsJsonAsync($"/api/identity/people/{id}/roles", new { roles = new[] { "employee" } });
        var owed = await OwedAsync(id);
        FailRevocations = false;
        var signIn = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));

        Assert.Equal(HttpStatusCode.InternalServerError, save.StatusCode);
        Assert.True(owed);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.False(await OwedAsync(id));
        Assert.Equal([new PrivilegesRevoked(id, "roles saved without supervisor"), new PrivilegesRevoked(id, "an earlier revocation retried")], Revoked);
    }

    /// <summary>
    /// D17: a revocation still owed from a failed sign-in is dropped, unpublished, by a later Microsoft sign-in whose
    /// mapping gives supervisor back — it would otherwise revoke the secrets issued after the re-grant.
    /// </summary>
    [Fact]
    public async Task AnOwedRevocation_IsDroppedByASignInThatGivesSupervisorBack()
    {
        await SaveSettingsAsync();
        await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors]));
        var id = (await PersonAsync(Email)).GetProperty("id").GetString()!;
        FailRevocations = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]))).StatusCode);
        FailRevocations = false;

        var regranted = await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Supervisors]));

        Assert.Equal(HttpStatusCode.Redirect, regranted.StatusCode);
        Assert.False(await OwedAsync(id));
        Assert.Equal([new PrivilegesRevoked(id, "Entra group mapping without supervisor")], Revoked);
    }

    /// <summary>
    /// QA-3 L1: every password attempt on a managed account counts, the right password too, so enough of them lock it
    /// out — and that lockout does not keep its owner from signing in with Microsoft.
    /// </summary>
    [Fact]
    public async Task RightPasswordsLockAManagedAccountOut_ButNotOutOfMicrosoftSignIn()
    {
        await SaveSettingsAsync();
        var (member, memberId) = await PasswordMemberAsync(Bob, role: "employee");
        await LinkAsync(member, Oid(3), [Billing]);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await PasswordSignInAsync(Bob, MemberPassword)).StatusCode);
        var lockedUntil = await ScalarAsync("""SELECT "LockoutEnd" FROM identity_users WHERE "Id" = @id""", memberId);
        var browser = new Browser(Host);
        var microsoft = await Flow.SignInAsync(browser, EntraFlow.Claims(Tenant, Oid(3), [Billing], email: "linked@contoso.example"));

        Assert.IsType<DateTime>(lockedUntil);
        Assert.Equal(HttpStatusCode.Redirect, microsoft.StatusCode);
        Assert.Equal((HttpStatusCode.OK, memberId), ((await MeAsync(browser)).Status, (await MeAsync(browser)).Id));
    }

    /// <summary>SEC L3: <c>xms_edov = false</c> — the tenant does not vouch for the email — creates no account; <c>true</c> does.</summary>
    [Fact]
    public async Task AnEmailWhoseDomainTheTenantDoesNotVerify_CreatesNoAccount()
    {
        await SaveSettingsAsync();
        var unverified = EntraFlow.Claims(Tenant, Oid(1), [Billing]);
        unverified["xms_edov"] = false;
        var verified = EntraFlow.Claims(Tenant, Oid(2), [Billing], email: "verified@contoso.example");
        verified["xms_edov"] = true;
        var before = await PeopleCountAsync();

        var refused = await Flow.SignInAsync(new Browser(Host), unverified);
        var created = await Flow.SignInAsync(new Browser(Host), verified);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("does not verify the email address", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(before + 1, await PeopleCountAsync());
    }

    /// <summary>CR L6: a long name is cut between whole characters, never inside a surrogate pair.</summary>
    [Fact]
    public async Task ALongDisplayName_IsCutBetweenWholeCharacters()
    {
        await SaveSettingsAsync();
        var name = new string('a', 99) + "\U0001F600" + "b";

        await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing], name: name));

        Assert.Equal(new string('a', 99), (await PersonAsync(Email)).GetProperty("displayName").GetString());
    }

    /// <summary>CR L2: Graph answering with something that is not its JSON is "try again" (502), not a 500.</summary>
    [Fact]
    public async Task Overage_WithAnUnreadableGraphAnswer_Is502()
    {
        await SaveSettingsAsync();
        Graph.CheckBody = "{ not json";
        var claims = EntraFlow.Claims(Tenant, Oid(1));
        claims["_claim_names"] = new { groups = "src1" };

        var failed = await Flow.SignInAsync(new Browser(Host), claims);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("could not read your groups", await failed.Content.ReadAsStringAsync());
    }

    /// <summary>The IdP refusing (the person cancelled, or consent was denied) comes back to the sign-in page cleanly: no 500, no session.</summary>
    [Fact]
    public async Task TheIdpAnsweringAccessDenied_EndsOnTheLoginPage()
    {
        await SaveSettingsAsync();
        var browser = new Browser(Host);
        var challenge = await browser.SubmitAsync("/Login?handler=Microsoft&returnUrl=%2FOrg", [], tokenFrom: "/Login");

        var callback = await browser.PostFormAsync(GuardedPaths.ExternalSignInCallback, new()
        {
            ["error"] = "access_denied", ["error_description"] = "AADSTS65004: User declined to consent.", ["state"] = EntraFlow.StateOf(challenge)
        }, request => request.Headers.Add("Origin", Idp.Origin));
        var completed = await EntraFlow.CompleteAsync(browser, callback);

        Assert.Equal("/Login?returnUrl=%2FOrg&handler=Microsoft", callback.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, completed.StatusCode);
        Assert.Contains("Microsoft sign-in did not complete", await completed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(browser)).Status);
    }

    /// <summary>CR L1: the handler's metadata and code redemption go through the factory's client, not one it made itself.</summary>
    [Fact]
    public async Task TheOidcBackchannel_IsTheFactorysClient()
    {
        await SaveSettingsAsync();

        await Flow.SignInAsync(new Browser(Host), EntraFlow.Claims(Tenant, Oid(1), [Billing]));

        Assert.Contains(Backchannel, u => u.AbsolutePath == $"/{Tenant}/.well-known/openid-configuration");
        Assert.Contains(Backchannel, u => u.AbsolutePath == $"/{Tenant}/token");
    }

    /// <summary>SEC L1: outside Development with no Data Protection certificate, a stored secret is flagged on the page and in the API.</summary>
    [Fact]
    public async Task AStoredSecret_WithUnencryptedKeys_IsWarnedAbout()
    {
        var before = await (await OwnerBrowser.GetAsync("/Entra")).Content.ReadAsStringAsync();
        await SaveSettingsAsync();

        var page = await (await OwnerBrowser.GetAsync("/Entra")).Content.ReadAsStringAsync();
        var settings = await Owner.GetFromJsonAsync<JsonElement>("/api/identity/entra/settings");

        Assert.DoesNotContain("stored unencrypted", before); // nothing stored yet
        Assert.Contains("stored unencrypted in the same database", page);
        Assert.True(settings.GetProperty("secretKeysUnencrypted").GetBoolean());
    }

    private const string Email = "bea@contoso.example";

    private const string EntraPasswordMessage = "This account signs in with Microsoft.";

    private const string MicrosoftHint = "If your organisation uses Microsoft sign-in, use the button below.";

    private static bool OffersRemoval(string page, string userId) =>
        page.Contains($"handler=RemoveMicrosoft&amp;id={userId}") || page.Contains($"id={userId}&amp;handler=RemoveMicrosoft");

    private static string KeyOf(string oid) => $"{Tenant}|{oid}";

    /// <summary>D13: a <c>PrivilegesRevoked</c> is owed to the account (its mark is in the database).</summary>
    private async Task<bool> OwedAsync(string userId) =>
        (long)(await ScalarAsync("""SELECT count(*) FROM identity_pending_revocations WHERE "UserId" = @id""", userId))! > 0;

    private async Task<int> FailedCountAsync(string userId) =>
        (int)(await ScalarAsync("""SELECT "AccessFailedCount" FROM identity_users WHERE "Id" = @id""", userId))!;

    private async Task<object?> ScalarAsync(string sql, string userId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", userId);
        return await command.ExecuteScalarAsync();
    }

    private MockIdentityProvider Idp { get; } = idp;

    private Task<HttpResponseMessage> PasswordSignInAsync(string email, string password) =>
        Host.Client().PostAsJsonAsync("/api/identity/sign-in", new { email, password, useCookie = false });

    /// <summary>Links Microsoft to the browser's password account (D10) and checks it worked.</summary>
    private async Task LinkAsync(Browser browser, string oid, string[] groups)
    {
        var challenge = await StartLinkAsync(browser);
        var linked = await EntraFlow.CompleteAsync(browser, await Flow.AtMicrosoftAsync(browser, challenge,
            EntraFlow.Claims(Tenant, oid, groups, email: "linked@contoso.example")));
        Assert.Contains("Your Microsoft account is linked", await linked.Content.ReadAsStringAsync());
    }
}
