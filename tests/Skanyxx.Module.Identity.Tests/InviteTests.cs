using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>D026: the owner invites by a single-use link; the invitee sets a password and gets the invite's roles.</summary>
[Collection(PostgresCollection.Name)]
public sealed class InviteTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly WarningLog _log = new();
    private readonly ManualClock _clock = new();
    private IdentityApp _app = null!;
    private string _owner = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:InviteDays"] = "2",
            services: s => s.AddSingleton<ILoggerProvider>(_log).AddSingleton<TimeProvider>(_clock));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Create_Returns201_WithTheLinkOnce_AndTheListHasNoToken()
    {
        var created = await _app.CreateInviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder, SkanyxxRoles.Employee);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var token = IdentityApp.TokenOf(body.GetProperty("link").GetString()!);
        var list = await _app.Client(bearer: _owner).GetStringAsync("/api/identity/invites", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
        Assert.Matches("^http://127\\.0\\.0\\.1:\\d+/Invite\\?token=skx_inv_[A-Za-z0-9_-]{43}$", body.GetProperty("link").GetString());
        Assert.Equal(_clock.GetUtcNow().AddDays(2), body.GetProperty("expiresAt").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
        Assert.Contains(IdentityApp.MemberEmail, list);
        Assert.Contains(body.GetProperty("inviteId").GetString()!, list);
        Assert.DoesNotContain(token, list);
        Assert.DoesNotContain(token[8..], list);
    }

    /// <summary>
    /// SEC M1: with Identity:PublicBaseUrl the link never comes from the request (scheme, Host, path base). SEC2 N3: it
    /// is built from the parsed URL, not the configured string.
    /// </summary>
    [Fact]
    public async Task Create_BuildsTheLinkOnTheConfiguredPublicBaseUrl()
    {
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, s => s["Identity:PublicBaseUrl"] = "HTTPS://People.Skanyxx.example:443/base/");
        var owner = (await app.SignInBearerAsync()).AccessToken;

        var created = await app.Client(bearer: owner).SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/identity/invites")
        {
            Content = JsonContent.Create(new { email = IdentityApp.MemberEmail, roles = new[] { SkanyxxRoles.Builder } }),
            Headers = { Host = "internal-service:8080" }
        }, TestContext.Current.CancellationToken);
        var link = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("link").GetString()!;

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Matches("^https://people\\.skanyxx\\.example/base/Invite\\?token=skx_inv_[A-Za-z0-9_-]{43}$", link);
    }

    [Fact]
    public async Task Lookup_ShowsEmailAndRoles_UnknownTokenIs404()
    {
        var token = await _app.InviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder);

        var found = await _app.LookupInviteAsync(token);
        var unknown = await _app.LookupInviteAsync("skx_inv_" + new string('A', 43));
        var details = await found.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(IdentityApp.MemberEmail, details.GetProperty("email").GetString());
        Assert.Equal(["builder", "supervisor"], details.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Accept_CreatesTheAccountWithTheRoles_SignsIn_AndIsSingleUse()
    {
        var token = await _app.InviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder);

        var accepted = await _app.AcceptAsync(token);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var access = body.GetProperty("tokens").GetProperty("accessToken").GetString();
        var probe = await _app.Client(bearer: access).GetFromJsonAsync<JsonElement>(IdentityApp.ProbePath, cancellationToken: TestContext.Current.CancellationToken);
        var again = await _app.AcceptAsync(token, "a different passphrase");
        var lookup = await _app.LookupInviteAsync(token);
        var signIn = await _app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(IdentityApp.MemberEmail, body.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Bea Builder", body.GetProperty("user").GetProperty("displayName").GetString());
        Assert.Equal(["builder"], probe.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.False(probe.GetProperty("supervisor").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal(2, await postgres.UserCountAsync());
    }

    [Fact]
    public async Task Accept_WithCookie_SetsTheSessionCookie()
    {
        var accepted = await _app.AcceptAsync(await _app.InviteAsync(_owner), useCookie: true);
        var me = await _app.Client(cookie: SetCookie.AuthHeader(accepted)).GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(IdentityApp.MemberEmail, me.GetProperty("email").GetString());
    }

    /// <summary>A rejected password rolls the spend back: the invitee can try again with the same link.</summary>
    [Fact]
    public async Task Accept_WithAShortPassword_Is400_AndTheLinkStillWorks()
    {
        var token = await _app.InviteAsync(_owner);

        var tooShort = await _app.AcceptAsync(token, "short");
        var retry = await _app.AcceptAsync(token);

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task ParallelAccepts_OfOneInvite_CreateExactlyOneAccount()
    {
        var token = await _app.InviteAsync(_owner);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => _app.AcceptAsync(token, $"parallel passphrase {i}")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(2, await postgres.UserCountAsync());
    }

    [Fact]
    public async Task ExpiredInvite_IsGone_ForLookupAcceptAndList()
    {
        var token = await _app.InviteAsync(_owner);

        _clock.Advance(TimeSpan.FromDays(2) + TimeSpan.FromSeconds(1));
        var lookup = await _app.LookupInviteAsync(token);
        var accept = await _app.AcceptAsync(token);
        var list = await _app.Client(bearer: (await _app.SignInBearerAsync()).AccessToken).GetFromJsonAsync<JsonElement>("/api/identity/invites", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal(0, list.GetArrayLength());
        Assert.Equal(1, await postgres.UserCountAsync());
    }

    [Fact]
    public async Task RevokedInvite_CannotBeAccepted_AndRevokingAgainIs404()
    {
        var created = await (await _app.CreateInviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Builder)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var id = created.GetProperty("inviteId").GetString();

        var revoke = await _app.Client(bearer: _owner).DeleteAsync($"/api/identity/invites/{id}", TestContext.Current.CancellationToken);
        var again = await _app.Client(bearer: _owner).DeleteAsync($"/api/identity/invites/{id}", TestContext.Current.CancellationToken);
        var accept = await _app.AcceptAsync(IdentityApp.TokenOf(created.GetProperty("link").GetString()!));

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task ReInvite_RevokesTheOlderInvite()
    {
        var first = await _app.InviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Employee);
        var second = await _app.InviteAsync(_owner, "Builder@Skanyxx.Example", SkanyxxRoles.Builder);

        var old = await _app.AcceptAsync(first);
        var list = await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/invites", cancellationToken: TestContext.Current.CancellationToken);
        var current = await _app.AcceptAsync(second);

        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, current.StatusCode);
    }

    [Theory]
    [InlineData(IdentityApp.OwnerEmail)]
    [InlineData("OWNER@skanyxx.example")]
    public async Task Invite_ForAnExistingAccount_Is409(string email)
    {
        var response = await _app.CreateInviteAsync(_owner, email, SkanyxxRoles.Builder);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("new@skanyxx.example", "owner")]
    [InlineData("new@skanyxx.example", "admin")]
    [InlineData("new@skanyxx.example", "Builder")]
    [InlineData("new@skanyxx.example")]
    [InlineData("not-an-email", "builder")]
    [InlineData("o'brien@skanyxx.example", "builder")]  // outside Identity's user name characters: could never be accepted
    [InlineData("b\u00e9a@skanyxx.example", "builder")]
    [InlineData("bea smith@skanyxx.example", "builder")]
    public async Task Invite_WithBadInput_Is400(string email, params string[] roles)
    {
        var response = await _app.CreateInviteAsync(_owner, email, roles);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheTokenIsStoredOnlyAsItsSha256()
    {
        var token = await _app.InviteAsync(_owner);

        await using var db = postgres.CreateDbContext();
        var row = await db.Database.SqlQuery<string>($"SELECT row_to_json(i)::text AS \"Value\" FROM identity_invites i").SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        var invite = await db.Invites.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), invite.TokenHash);
        Assert.DoesNotContain(token[8..], row);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(token)), row);
    }

    /// <summary>Invite, revoke, accept, role change and disable are audited at Warning with ids, never with the token.</summary>
    [Fact]
    public async Task AdminActions_AreLoggedAtWarning_WithoutTokens()
    {
        var ownerId = (await postgres.OwnerAsync()).Id;
        var revokedToken = await _app.InviteAsync(_owner, "someone@skanyxx.example");
        var invites = await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/invites", cancellationToken: TestContext.Current.CancellationToken);
        await _app.Client(bearer: _owner).DeleteAsync($"/api/identity/invites/{invites[0].GetProperty("id").GetString()}", TestContext.Current.CancellationToken);
        var token = await _app.InviteAsync(_owner);
        var (member, memberId) = await AcceptedAsync(token);
        await _app.Client(bearer: _owner).PutAsJsonAsync($"/api/identity/people/{memberId}/roles", new { roles = new[] { "employee" } }, cancellationToken: TestContext.Current.CancellationToken);
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);

        var warnings = _log.Warnings;
        Assert.Contains(warnings, w => w.Contains($"created by {ownerId} from 127.0.0.1"));
        Assert.Contains(warnings, w => w.Contains($"revoked by {ownerId} from 127.0.0.1"));
        Assert.Contains(warnings, w => w.Contains($"accepted from 127.0.0.1: account {memberId}"));
        Assert.Contains(warnings, w => w.Contains($"Roles of {memberId} changed by {ownerId} from 127.0.0.1"));
        Assert.Contains(warnings, w => w.Contains($"Account {memberId} disabled by {ownerId} from 127.0.0.1"));
        Assert.All(warnings, w => Assert.DoesNotContain(token[8..], w));
        Assert.All(warnings, w => Assert.DoesNotContain(revokedToken[8..], w));
        Assert.All(warnings, w => Assert.DoesNotContain(member.AccessToken, w));
    }

    /// <summary>SEC L6: refused accepts and lookups leave a Warning (with the invite id when one was found), never the token.</summary>
    [Fact]
    public async Task RefusedAcceptsAndLookups_AreLoggedAtWarning_WithoutTokens()
    {
        var unknown = "skx_inv_" + new string('B', 43);
        var token = await _app.InviteAsync(_owner);
        await _app.AcceptAsync(token);
        var conflicting = await _app.InviteAsync(_owner, "later@skanyxx.example");
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            await users.CreateAsync(new IdentityUser { UserName = "later@skanyxx.example", Email = "later@skanyxx.example" });
        }

        var refusedUnknown = await _app.AcceptAsync(unknown);
        var refusedSpent = await _app.AcceptAsync(token);
        var refusedConflict = await _app.AcceptAsync(conflicting);
        var lookup = await _app.LookupInviteAsync(unknown);

        Assert.Equal(HttpStatusCode.NotFound, refusedUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refusedSpent.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refusedConflict.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        var warnings = _log.Warnings;
        Assert.Equal(2, warnings.Count(w => w.Contains("Invite accept refused from 127.0.0.1: invite invalid")));
        Assert.Contains(warnings, w => w.Contains("Invite accept refused from 127.0.0.1: invite ") && w.Contains("an account with the email exists"));
        Assert.Contains(warnings, w => w.Contains("Invite lookup refused from 127.0.0.1: invalid"));
        Assert.All(warnings, w => Assert.DoesNotContain(unknown[8..], w));
        Assert.All(warnings, w => Assert.DoesNotContain(token[8..], w));
        Assert.All(warnings, w => Assert.DoesNotContain(conflicting[8..], w));
    }

    /// <summary>SEC L6: a signed-in non-owner on an owner-only route is logged at Warning with actor and route.</summary>
    /// <summary>
    /// SEC L6 / CR2-m2 / SEC2 N5: a refusal is logged only for someone holding none of the route's roles (a supervisor on
    /// an owner-or-supervisor route is allowed, not refused), and with the route template, never the caller's path.
    /// </summary>
    [Fact]
    public async Task OwnerRoute_RefusedForANonOwner_IsLoggedAtWarning()
    {
        var (member, memberId) = await AcceptedAsync(await _app.InviteAsync(_owner, IdentityApp.MemberEmail, SkanyxxRoles.Supervisor));

        var shared = await _app.Client(bearer: member.AccessToken).GetAsync(IdentityApp.SupervisorProbePath, TestContext.Current.CancellationToken);
        var refused = await _app.Client(bearer: member.AccessToken).GetAsync("/api/identity/people", TestContext.Current.CancellationToken);
        var forged = await _app.Client(bearer: member.AccessToken).PutAsJsonAsync("/api/identity/people/x%0D%0Afake-line/roles", new { roles = Array.Empty<string>() }, cancellationToken: TestContext.Current.CancellationToken);
        var allowed = await _app.Client(bearer: _owner).GetAsync("/api/identity/people", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(2, _log.Warnings.Count(w => w.Contains("Owner-only route")));
        Assert.Contains(_log.Warnings, w => w.Contains($"Owner-only route GET api/identity/people refused for {memberId} from 127.0.0.1"));
        Assert.Contains(_log.Warnings, w => w.Contains($"Owner-only route PUT api/identity/people/{{id}}/roles refused for {memberId}"));
        Assert.DoesNotContain(_log.Warnings, w => w.Contains("fake-line") || w.Contains("probe"));
    }

    /// <summary>SEC L1: only a pending invite takes the email's account lock, so an old link cannot keep sign-ins busy.</summary>
    [Fact]
    public async Task SpentOrRevokedToken_DoesNotWaitForTheAccountLock()
    {
        var spent = await _app.InviteAsync(_owner);
        Assert.Equal(HttpStatusCode.Created, (await _app.AcceptAsync(spent)).StatusCode);
        var revoked = await _app.InviteAsync(_owner, "other@skanyxx.example");
        await _app.Client(bearer: _owner).DeleteAsync($"/api/identity/invites/{(await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/invites", cancellationToken: TestContext.Current.CancellationToken))[0].GetProperty("id").GetString()}", TestContext.Current.CancellationToken);

        await using var db = postgres.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        Assert.True(await AccountLock.TryAcquireAsync(db, IdentityApp.MemberEmail.ToUpperInvariant(), CancellationToken.None));
        Assert.True(await AccountLock.TryAcquireAsync(db, "OTHER@SKANYXX.EXAMPLE", CancellationToken.None));

        var spentAgain = await _app.AcceptAsync(spent).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var revokedAccept = await _app.AcceptAsync(revoked).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, spentAgain.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedAccept.StatusCode);
    }

    /// <summary>SEC L4: bidi overrides and zero-width characters would let a name pass for someone else's.</summary>
    [Theory]
    [InlineData("\u202Eroot")]
    [InlineData("Ow\u200Bner")]
    [InlineData("Bea\u2066")]
    public async Task DisplayName_WithFormatCharacters_Is400(string displayName)
    {
        var token = await _app.InviteAsync(_owner);

        var response = await _app.Client().PostAsJsonAsync("/api/identity/invites/accept",
            new { token, password = IdentityApp.MemberPassword, displayName }, cancellationToken: TestContext.Current.CancellationToken);
        var stillOpen = await _app.LookupInviteAsync(token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Display name", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
    }

    private async Task<(Tokens Tokens, string UserId)> AcceptedAsync(string token)
    {
        var body = await (await _app.AcceptAsync(token)).Content.ReadFromJsonAsync<JsonElement>();
        return (Tokens.From(body.GetProperty("tokens")), body.GetProperty("user").GetProperty("id").GetString()!);
    }
}
