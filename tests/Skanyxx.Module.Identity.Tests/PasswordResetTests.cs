using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Email;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;
using Skanyxx.Module.Identity.Features.Passwords;
using Skanyxx.Module.Identity.Passwords;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D156/D157: "forgot your password?" answers the same for every email and emails a single-use, short-lived, hashed
/// link only to an account that may reset; the owner can issue a link from People; completing it sets the password,
/// clears the lockout and ends every session. The owner, disabled accounts and Entra-managed accounts while Microsoft
/// sign-in is on cannot reset.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PasswordResetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string NewPassword = "a brand new passphrase";
    private const string Other = "other@skanyxx.example";

    private readonly FakeEmailSender _mail = new();
    private IdentityApp _app = null!;
    private string _owner = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s.AddSingleton<IEmailSender>(_mail));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task UnknownAndKnownEmails_GetTheSameAnswer_AndOnlyTheAccountGetsTheLink()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);
        var status = await _app.Client().GetFromJsonAsync<JsonElement>("/api/identity/status", cancellationToken: TestContext.Current.CancellationToken);

        var unknown = await ForgotAsync("nobody@skanyxx.example");
        var known = await ForgotAsync(IdentityApp.MemberEmail);
        var email = await _mail.NextAsync();
        var token = FakeEmailSender.TokenIn(email);

        Assert.True(status.GetProperty("passwordResetByEmail").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), await known.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(_mail.Resets); // FIFO: the unknown email was handled first and sent nothing
        Assert.Equal(IdentityApp.MemberEmail, email.To);
        Assert.Matches("^skx_rst_[A-Za-z0-9_-]{43}$", token); // 256 bits
        Assert.DoesNotContain(IdentityApp.MemberPassword, email.TextBody);
        await using var db = postgres.CreateDbContext();
        var row = await db.PasswordResets.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), row.TokenHash); // only the hash is stored
        Assert.Equal(memberId, row.UserId);
        Assert.Null(row.CreatedBy);
        Assert.InRange(row.ExpiresUtc - row.CreatedUtc, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
        var requested = Assert.Single(await postgres.AuditAsync("password.reset_requested"));
        Assert.Equal(memberId, requested.TargetId);
        Assert.Null(requested.ActorId);
        Assert.Single(await postgres.AuditAsync("password.reset_refused"), r => r.TargetId is null && r.Details!.Contains("no such account"));
    }

    [Fact]
    public async Task TheLink_SetsTheNewPassword_Once_EndsEverySession_AndClearsTheLockout()
    {
        var (tokens, memberId) = await _app.AddMemberAsync(_owner);
        for (var i = 0; i < 5; i++)
            await _app.SignInAsync(IdentityApp.MemberEmail, "wrong password " + i);
        var lockedOut = await _app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword);
        await ForgotAsync(IdentityApp.MemberEmail);
        var token = FakeEmailSender.TokenIn(await _mail.NextAsync());

        var lookup = await LookupAsync(token);
        var reset = await ResetAsync(token, NewPassword);
        var oldBearer = await _app.Client(bearer: tokens.AccessToken).GetAsync(IdentityApp.ProbePath, TestContext.Current.CancellationToken);
        var oldRefresh = await _app.RefreshAsync(tokens.RefreshToken);
        var oldPassword = await _app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword);
        var newPassword = await _app.SignInAsync(IdentityApp.MemberEmail, NewPassword);
        var reused = await ResetAsync(token, "yet another passphrase");
        var lookupAfter = await LookupAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.Equal(IdentityApp.MemberEmail, (await lookup.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode); // the lockout is cleared
        Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookupAfter.StatusCode);
        var completed = Assert.Single(await postgres.AuditAsync("password.reset_completed"));
        Assert.Equal(memberId, completed.TargetId);
        // The chains are dropped, not merely refused by the rotated stamp: nothing of the old session is left to reuse.
        await using var db = postgres.CreateDbContext();
        Assert.Single(await db.RefreshSessions.Where(r => r.UserId == memberId).ToListAsync(cancellationToken: TestContext.Current.CancellationToken)); // only the new sign-in's
    }

    [Fact]
    public async Task AnExpiredLink_IsRefused_AndThePasswordStays()
    {
        await _app.AddMemberAsync(_owner);
        await ForgotAsync(IdentityApp.MemberEmail);
        var token = FakeEmailSender.TokenIn(await _mail.NextAsync());
        await using (var db = postgres.CreateDbContext())
            await db.PasswordResets.ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresUtc, DateTimeOffset.UtcNow.AddSeconds(-1)), cancellationToken: TestContext.Current.CancellationToken);

        var lookup = await LookupAsync(token);
        var reset = await ResetAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        Assert.Contains(PasswordResetStatusHandler.Invalid, await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, (await _app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword)).StatusCode);
    }

    [Fact]
    public async Task ANewerLink_RevokesTheOlderOne()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);

        var first = await IssueAsync(memberId);
        var firstToken = FakeEmailSender.TokenIn(await _mail.NextAsync());
        await IssueAsync(memberId);
        var secondToken = FakeEmailSender.TokenIn(await _mail.NextAsync());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResetAsync(firstToken, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(secondToken, NewPassword)).StatusCode);
    }

    /// <summary>One email per account per two minutes: the form cannot be used to flood someone's inbox.</summary>
    [Fact]
    public async Task ASecondRequestSoonAfter_SendsNothing()
    {
        await _app.AddMemberAsync(_owner);
        await _app.AddMemberAsync(_owner, Other);

        await ForgotAsync(IdentityApp.MemberEmail);
        var first = await _mail.NextAsync();
        var again = await ForgotAsync(IdentityApp.MemberEmail);
        await ForgotAsync(Other);
        var second = await _mail.NextAsync();

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(IdentityApp.MemberEmail, first.To);
        Assert.Equal(Other, second.To);
        Assert.Equal(2, _mail.Resets.Count);
    }

    /// <summary>D157: the owner, a disabled account and an Entra-managed one while Microsoft sign-in is on get no link either way.</summary>
    [Fact]
    public async Task TheOwner_DisabledAndManagedAccounts_CannotReset()
    {
        var ownerId = (await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/me", cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
        var (_, disabledId) = await _app.AddMemberAsync(_owner);
        var (_, managedId) = await _app.AddMemberAsync(_owner, "managed@skanyxx.example");
        await _app.AddMemberAsync(_owner, Other);
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{disabledId}/disable", null, TestContext.Current.CancellationToken);
        await ManageAsync(managedId, entraOn: true);

        await ForgotAsync(IdentityApp.OwnerEmail);
        await ForgotAsync(IdentityApp.MemberEmail);
        await ForgotAsync("managed@skanyxx.example");
        await ForgotAsync(Other);
        var only = await _mail.NextAsync();
        var owner = await IssueAsync(ownerId);
        var disabled = await IssueAsync(disabledId);
        var managed = await IssueAsync(managedId);
        await ManageAsync(managedId, entraOn: false);
        var managedWhileOff = await IssueAsync(managedId);

        Assert.Equal(Other, only.To); // the three before it sent nothing
        Assert.Equal(HttpStatusCode.Forbidden, owner.StatusCode);
        Assert.Contains("bootstrap token", await owner.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, managed.StatusCode);
        Assert.Contains(EntraPasswordRule.Message, await managed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Created, managedWhileOff.StatusCode);
        Assert.Equal(3, (await postgres.AuditAsync("password.reset_refused")).Count(r => r.TargetId is not null));
    }

    /// <summary>D165: a disable revokes the person's open links in its own transaction; the link answers like any dead one.</summary>
    [Fact]
    public async Task ALinkIssuedBeforeADisable_IsRevokedByIt_AndTheAccountStaysDisabled()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);
        await IssueAsync(memberId);
        var token = FakeEmailSender.TokenIn(await _mail.NextAsync());
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);

        var reset = await ResetAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.SignInAsync(IdentityApp.MemberEmail, NewPassword)).StatusCode);
        await using var db = postgres.CreateDbContext();
        Assert.Equal(new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero), (await db.Users.SingleAsync(u => u.Id == memberId, cancellationToken: TestContext.Current.CancellationToken)).LockoutEnd);
        var row = await db.PasswordResets.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(row.UsedUtc);
        Assert.NotNull(row.RevokedUtc);
        Assert.Contains("resetLinksRevoked", Assert.Single(await postgres.AuditAsync("person.disabled")).Details);
    }

    /// <summary>CR M1: a compromised mailbox's link must not come back when the owner enables the account again.</summary>
    [Fact]
    public async Task ALink_DoesNotSurviveADisableAndEnable()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);
        await IssueAsync(memberId);
        var token = FakeEmailSender.TokenIn(await _mail.NextAsync());

        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/disable", null, TestContext.Current.CancellationToken);
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken);
        var lookup = await LookupAsync(token);
        var reset = await ResetAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _app.SignInAsync(IdentityApp.MemberEmail, IdentityApp.MemberPassword)).StatusCode);
    }

    /// <summary>
    /// CR M1, the completion side: a link used while its account is refused (here disabled behind the handler's back) is
    /// revoked by that refusal, so a later enable does not bring it back.
    /// </summary>
    [Fact]
    public async Task ALinkRefusedAtCompletion_IsRevoked_AndStaysDeadAfterAnEnable()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);
        await IssueAsync(memberId);
        var token = FakeEmailSender.TokenIn(await _mail.NextAsync());
        await using (var db = postgres.CreateDbContext())
            await db.Users.Where(u => u.Id == memberId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero)), cancellationToken: TestContext.Current.CancellationToken);

        var refused = await ResetAsync(token, NewPassword);
        await _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/enable", null, TestContext.Current.CancellationToken);
        var again = await ResetAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        await using var after = postgres.CreateDbContext();
        Assert.NotNull((await after.PasswordResets.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).RevokedUtc);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.SignInAsync(IdentityApp.MemberEmail, NewPassword)).StatusCode);
    }

    /// <summary>A rejected password changes nothing — the link still works — and leaves no "completed" row.</summary>
    [Fact]
    public async Task ARejectedPassword_KeepsTheLinkUsable()
    {
        await _app.AddMemberAsync(_owner);
        await ForgotAsync(IdentityApp.MemberEmail);
        var token = FakeEmailSender.TokenIn(await _mail.NextAsync());

        var tooShort = await ResetAsync(token, "short");
        var containsEmail = await ResetAsync(token, "builder and some more words");
        var completedBefore = await postgres.AuditAsync("password.reset_completed");
        var good = await ResetAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, containsEmail.StatusCode);
        Assert.Contains("before the @", await containsEmail.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(completedBefore);
        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _app.SignInAsync(IdentityApp.MemberEmail, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task TheOwnersLink_IsEmailed_AndNotShown_OrShownWhenSendingFails()
    {
        var (_, memberId) = await _app.AddMemberAsync(_owner);

        var emailed = await (await IssueAsync(memberId)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var sent = await _mail.NextAsync();
        _mail.Fail = true;
        var failed = await (await IssueAsync(memberId)).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(emailed.GetProperty("emailed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, emailed.GetProperty("link").ValueKind);
        Assert.Equal(IdentityApp.MemberEmail, sent.To);
        Assert.False(failed.GetProperty("emailed").GetBoolean());
        var link = failed.GetProperty("link").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(IdentityApp.TokenOf(link), NewPassword)).StatusCode);
        var issued = await postgres.AuditAsync("password.reset_issued");
        Assert.Equal(2, issued.Count);
        Assert.All(issued, r => Assert.Equal(memberId, r.TargetId));
        // CR L1: the second row said "email"; this one records that the owner was shown the link after all.
        var shown = Assert.Single(await postgres.AuditAsync("password.reset_link_shown"));
        Assert.Equal(memberId, shown.TargetId);
        Assert.NotNull(shown.ActorId);
        Assert.True(shown.Id > issued[1].Id);
    }

    /// <summary>Without SMTP nothing changes for sign-in; "forgot" is unavailable (a configuration answer, not an account one), the owner's link is shown.</summary>
    [Fact]
    public async Task WithoutSmtp_ForgotIsUnavailable_AndTheOwnerGetsTheLink()
    {
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString);
        var (_, memberId) = await app.AddMemberAsync(_owner);

        var status = await app.Client().GetFromJsonAsync<JsonElement>("/api/identity/status", cancellationToken: TestContext.Current.CancellationToken);
        var forgot = await app.Client().PostAsJsonAsync("/api/identity/password/forgot", new { email = IdentityApp.MemberEmail }, cancellationToken: TestContext.Current.CancellationToken);
        var issued = await (await app.Client(bearer: _owner).PostAsync($"/api/identity/people/{memberId}/password-reset", null, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var reset = await app.Client().PostAsJsonAsync("/api/identity/password/reset",
            new { token = IdentityApp.TokenOf(issued.GetProperty("link").GetString()!), password = NewPassword }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(status.GetProperty("passwordResetByEmail").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, forgot.StatusCode);
        Assert.Contains(RequestPasswordResetHandler.Unavailable, await forgot.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False(issued.GetProperty("emailed").GetBoolean());
        Assert.Matches("/ResetPassword\\?token=skx_rst_", issued.GetProperty("link").GetString());
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("employee")]
    [InlineData("builder")]
    [InlineData("supervisor")]
    public async Task OnlyTheOwner_IssuesLinks(string? role)
    {
        var (_, targetId) = await _app.AddMemberAsync(_owner, Other);
        var caller = role is null ? null : (await _app.AddMemberAsync(_owner, IdentityApp.MemberEmail, role)).Tokens.AccessToken;

        var response = await _app.Client(bearer: caller).PostAsync($"/api/identity/people/{targetId}/password-reset", null, TestContext.Current.CancellationToken);

        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_mail.Resets);
        await using var db = postgres.CreateDbContext();
        Assert.False(await db.PasswordResets.AnyAsync(cancellationToken: TestContext.Current.CancellationToken));
        if (role is not null)
            Assert.Single(await postgres.AuditAsync("route.owner_refused"));
    }

    /// <summary>"Forgot" counts in the sign-in window; the reset link's lookup and reset in the one-time-link window.</summary>
    [Fact]
    public async Task ForgotAndTheLink_AreRateLimited()
    {
        await using var app = await IdentityApp.StartAsync(postgres.ConnectionString, s =>
        {
            s["Skanyxx:SignInRateLimit:PermitLimit"] = "2";
            s["Skanyxx:SignInRateLimit:WindowSeconds"] = "600";
            s["Skanyxx:InviteRateLimit:PermitLimit"] = "2";
            s["Skanyxx:InviteRateLimit:WindowSeconds"] = "600";
        }, services: s => s.AddSingleton<IEmailSender>(_mail));

        var forgot = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            forgot.Add((await app.Client().PostAsJsonAsync("/api/identity/password/forgot", new { email = "x@skanyxx.example" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var lookup = (await app.Client().PostAsJsonAsync("/api/identity/password/lookup", new { token = "skx_rst_nope" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode;
        var reset = (await app.Client().PostAsJsonAsync("/api/identity/password/reset", new { token = "skx_rst_nope", password = NewPassword }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode;
        var third = (await app.Client().PostAsJsonAsync("/api/identity/password/lookup", new { token = "skx_rst_nope" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode;

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], forgot);
        Assert.Equal(HttpStatusCode.NotFound, lookup);
        Assert.Equal(HttpStatusCode.NotFound, reset);
        Assert.Equal(HttpStatusCode.TooManyRequests, third);
    }

    private Task<HttpResponseMessage> ForgotAsync(string email) =>
        _app.Client().PostAsJsonAsync("/api/identity/password/forgot", new { email });

    private Task<HttpResponseMessage> LookupAsync(string token) =>
        _app.Client().PostAsJsonAsync("/api/identity/password/lookup", new { token });

    private Task<HttpResponseMessage> ResetAsync(string token, string password) =>
        _app.Client().PostAsJsonAsync("/api/identity/password/reset", new { token, password });

    private Task<HttpResponseMessage> IssueAsync(string userId) =>
        _app.Client(bearer: _owner).PostAsync($"/api/identity/people/{userId}/password-reset", null);

    /// <summary>Gives the account a Microsoft login and sets the owner's switch, as D8 reads it (the settings row).</summary>
    private async Task ManageAsync(string userId, bool entraOn)
    {
        await using var db = postgres.CreateDbContext();
        if (!await db.UserLogins.AnyAsync(l => l.UserId == userId))
            db.UserLogins.Add(new IdentityUserLogin<string>
            {
                LoginProvider = EntraScheme.Name, ProviderKey = "11111111-1111-1111-1111-111111111111|aaaaaaaa-0000-0000-0000-000000000009",
                ProviderDisplayName = "Microsoft", UserId = userId
            });
        if (await db.EntraSettings.SingleOrDefaultAsync() is { } row)
            row.Enabled = entraOn;
        else
            db.EntraSettings.Add(new EntraSettings { Enabled = entraOn, UpdatedBy = "test" });
        await db.SaveChangesAsync();
    }
}
