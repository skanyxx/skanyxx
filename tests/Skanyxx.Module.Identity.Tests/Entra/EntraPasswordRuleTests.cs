using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>
/// QA round 2 of slice 4 on the password side of an Entra-managed account: D8 follows the owner's switch, not whether
/// the secret still decrypts (D14); turning Microsoft sign-in on ends the managed accounts' sessions (D15); a managed
/// account's password is answered like a wrong one and neither resets nor spares the failed count (SEC N3); a refused
/// refresh is a Warning (SEC N6).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EntraPasswordRuleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Managed = "managed@skanyxx.example";
    private const string Local = "local@skanyxx.example";
    private const string Key = "11111111-1111-1111-1111-111111111111|aaaaaaaa-0000-0000-0000-000000000001";

    private readonly AllLog _log = new();
    private IdentityApp _app = null!;
    private string _owner = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.ResetAsync();
        _app = await IdentityApp.StartAsync(postgres.ConnectionString, services: s => s.AddSingleton<ILoggerProvider>(_log));
        Assert.Equal(HttpStatusCode.Created, (await _app.BootstrapAsync()).StatusCode);
        _owner = (await _app.SignInBearerAsync()).AccessToken;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>D14: a stored secret nobody can decrypt turns Microsoft sign-in off, but not D8 — only the owner's switch does.</summary>
    [Fact]
    public async Task AnUndecryptableSecret_DoesNotHandManagedAccountsTheirPasswordsBack()
    {
        var (tokens, managedId) = await _app.AddMemberAsync(_owner, Managed);
        await SaveAsync(enabled: true);
        await AddLoginAsync(managedId);
        await ExecuteAsync("""UPDATE identity_entra_settings SET "ProtectedClientSecret" = 'no key can read this'""");
        var saved = await SaveAsync(enabled: true, secret: null); // keeps the stored secret, reloads this instance

        var password = await _app.SignInAsync(Managed, IdentityApp.MemberPassword);
        var refresh = await _app.RefreshAsync(tokens.RefreshToken);
        await SaveAsync(enabled: false, secret: null);
        var passwordWhileOff = await _app.SignInAsync(Managed, IdentityApp.MemberPassword);

        Assert.True(saved.GetProperty("enabled").GetBoolean());
        Assert.False(saved.GetProperty("active").GetBoolean()); // sign-in is off: nothing can reach Microsoft
        Assert.Equal(HttpStatusCode.Unauthorized, password.StatusCode);
        Assert.Contains(SignInFailure.Message, await password.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, passwordWhileOff.StatusCode);
    }

    /// <summary>SEC N6: a refused refresh names the account and the address at Warning.</summary>
    [Fact]
    public async Task ARefusedRefresh_IsLoggedAtWarning()
    {
        var (tokens, managedId) = await _app.AddMemberAsync(_owner, Managed);
        await SaveAsync(enabled: true);
        await AddLoginAsync(managedId);

        var refresh = await _app.RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        var line = Assert.Single(_log.Lines, l => l.Contains("Refresh of") && l.Contains("refused"));
        Assert.StartsWith("Warning:", line);
        Assert.Contains(managedId, line);
        Assert.Contains("from ", line);
    }

    /// <summary>
    /// SEC N3 + QA-3 L1: the right password of a managed account gets the wrong-password answer, and counts toward the
    /// lockout exactly like a wrong one, so both do the same database work and log the same Warning.
    /// </summary>
    [Fact]
    public async Task AManagedAccountsPassword_IsAnsweredLikeAWrongOne_AndCountedLikeOne()
    {
        var (_, managedId) = await _app.AddMemberAsync(_owner, Managed);
        await SaveAsync(enabled: true);
        await AddLoginAsync(managedId);

        var wrong = await _app.SignInAsync(Managed, "not the password at all");
        var afterWrong = await FailedCountAsync(managedId);
        var right = await _app.SignInAsync(Managed, IdentityApp.MemberPassword);
        var afterRight = await FailedCountAsync(managedId);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode);
        Assert.Equal(await DetailAsync(wrong), await DetailAsync(right));
        Assert.Equal(1, afterWrong);
        Assert.Equal(2, afterRight);
        // QA-2 L1: the same Warning for both, so the log level does not tell the branches apart either.
        var warnings = _log.Lines.Where(l => l.StartsWith("Warning:") && l.Contains($"Password sign-in of {managedId}")).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains("signs in with Microsoft", warnings[0]);
        Assert.Equal(warnings[0], warnings[1]);
        // CR M2 / D166: the same audit row for both, so the right password costs no extra round trip; nothing says which.
        await using var db = postgres.CreateDbContext();
        var rows = await db.Audit.Where(a => a.Action == "signin.managed_password_refused").OrderBy(a => a.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(managedId, r.TargetId);
            Assert.Null(r.ActorId);
            Assert.Null(r.Details);
        });
    }

    /// <summary>
    /// QA-3 L2: D8 reads the switch from the database, so a replica whose cached settings still say "off" (the row was
    /// turned on elsewhere, same version: this instance does not reload) refuses the managed account's password and refresh.
    /// </summary>
    [Fact]
    public async Task D8_FollowsTheDatabase_NotAStaleCache()
    {
        var (tokens, managedId) = await _app.AddMemberAsync(_owner, Managed);
        await SaveAsync(enabled: false);
        await AddLoginAsync(managedId);
        await ExecuteAsync("""UPDATE identity_entra_settings SET "Enabled" = true""");

        var password = await _app.SignInAsync(Managed, IdentityApp.MemberPassword);
        var refresh = await _app.RefreshAsync(tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, password.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
    }

    /// <summary>
    /// D15: turning Microsoft sign-in on (off → on) ends every session of every managed account but the owner's —
    /// refresh chains and stamps — with one audit line and its count; a save that keeps it on ends nothing.
    /// </summary>
    [Fact]
    public async Task TurningMicrosoftSignInOn_EndsTheManagedAccountsSessions_OnlyThose()
    {
        var (managed, managedId) = await _app.AddMemberAsync(_owner, Managed);
        var (local, _) = await _app.AddMemberAsync(_owner, Local);
        await AddLoginAsync(managedId);
        await AddLoginAsync(await OwnerIdAsync(), "11111111-1111-1111-1111-111111111111|aaaaaaaa-0000-0000-0000-00000000000f");
        var stamp = await StampAsync(managedId);

        await SaveAsync(enabled: true);
        var managedAccess = await _app.Client(bearer: managed.AccessToken).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);
        var managedRefresh = await _app.RefreshAsync(managed.RefreshToken);
        var localAccess = await _app.Client(bearer: local.AccessToken).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);
        var ownerAccess = await _app.Client(bearer: _owner).GetAsync("/api/identity/me", TestContext.Current.CancellationToken);
        var turnedOn = _log.Lines.Where(l => l.Contains("Microsoft sign-in turned on")).ToList();
        await SaveAsync(enabled: true, secret: null);
        await SaveAsync(enabled: true, secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, managedAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, managedRefresh.StatusCode); // the chain is gone, not just refused
        Assert.NotEqual(stamp, await StampAsync(managedId));
        Assert.Equal(HttpStatusCode.OK, localAccess.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerAccess.StatusCode);
        Assert.StartsWith("Warning:", Assert.Single(turnedOn));
        Assert.Contains("the sessions of 1 Entra-managed accounts ended", turnedOn[0]);
        Assert.Single(_log.Lines, l => l.Contains("Microsoft sign-in turned on")); // on → on: nothing more
    }

    private async Task<JsonElement> SaveAsync(bool enabled, string? secret = EntraSettingsTests.Secret)
    {
        var response = await _app.Client(bearer: _owner).PutAsJsonAsync("/api/identity/entra/settings", EntraSettingsTests.Settings(enabled: enabled, secret: secret));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    private async Task AddLoginAsync(string userId, string key = Key)
    {
        await using var db = postgres.CreateDbContext();
        db.UserLogins.Add(new IdentityUserLogin<string> { LoginProvider = "entra", ProviderKey = key, ProviderDisplayName = "Microsoft", UserId = userId });
        await db.SaveChangesAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var db = postgres.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<int> FailedCountAsync(string userId)
    {
        await using var db = postgres.CreateDbContext();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.AccessFailedCount).SingleAsync();
    }

    private async Task<string?> StampAsync(string userId)
    {
        await using var db = postgres.CreateDbContext();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync();
    }

    private async Task<string> OwnerIdAsync() =>
        (await _app.Client(bearer: _owner).GetFromJsonAsync<JsonElement>("/api/identity/me")).GetProperty("id").GetString()!;
}
