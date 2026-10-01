using System.Net.Http.Json;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Tests.Entra;

/// <summary>
/// Races staged deterministically: the test holds an advisory lock, waits until the app's call is seen queued on that
/// very lock in <c>pg_locks</c> (CR m5), changes the data itself, then lets go.
/// <list type="bullet">
/// <item>CR L4 / SEC I2: a link and a create racing for one Microsoft identity (<c>tid|oid</c>) are serialized on it, so
/// the second answers for the first's login (409 "linked elsewhere", or the existing account) instead of a 500.</item>
/// <item>CR m1: a sign-in queued on the account lock while the owner removes its Microsoft login changes nothing.</item>
/// <item>CR m2: a password step-up never queues on the account lock.</item>
/// </list>
/// </summary>
public sealed class EntraLoginRaceTests(PostgresFixture postgres) : IdentityTestBase(postgres)
{
    private const string Key = "11111111-1111-1111-1111-111111111111|aaaaaaaa-0000-0000-0000-000000000001";
    private static readonly EntraMapping Employee = new(EntraAccountKind.Member, 1, [SkanyxxRoles.Employee], []);
    private static readonly EntraMapping Supervisor = new(EntraAccountKind.Member, 1, [SkanyxxRoles.Supervisor, SkanyxxRoles.Builder], []);

    private string _owner = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Assert.Equal(HttpStatusCode.Created, (await App.BootstrapAsync()).StatusCode);
        _owner = (await App.SignInBearerAsync()).AccessToken;
    }

    [Fact]
    public async Task ALinkQueuedBehindAnotherBind_Is409LinkedElsewhere()
    {
        var (_, linking) = await App.AddMemberAsync(_owner, "linking@skanyxx.example");
        var (_, other) = await App.AddMemberAsync(_owner, "other@skanyxx.example");

        var outcome = await RaceAsync(Key, EntraAccounts.LoginLockSeed,
            accounts => accounts.LinkAsync(linking, Key, Employee, CancellationToken.None), holder => BindAsync(holder, other));

        Assert.Equal(OutcomeStatus.Conflict, outcome.Status);
        Assert.Equal(EntraAccounts.LinkedElsewhere, outcome.Message);
        Assert.Empty(await LoginsOfAsync(linking));
    }

    [Fact]
    public async Task ACreateQueuedBehindALink_AnswersWithThatAccount_AndCreatesNone()
    {
        var (_, other) = await App.AddMemberAsync(_owner, "other@skanyxx.example");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(EntraClaims.Email, "new@contoso.example")], "test"));
        await using var db = Postgres.CreateDbContext();
        var people = db.Users.Count();

        var outcome = await RaceAsync(Key, EntraAccounts.LoginLockSeed, accounts =>
            accounts.CreateAsync(new ExternalLoginInfo(principal, EntraScheme.Name, Key, EntraScheme.DisplayName), Employee, CancellationToken.None),
            holder => BindAsync(holder, other));

        Assert.Equal(OutcomeStatus.Ok, outcome.Status);
        Assert.Equal(other, outcome.Value!.Id);
        Assert.Equal(people, db.Users.Count());
    }

    /// <summary>CR m1: the login was found before the lock and removed while the sign-in waited: no re-map, no session.</summary>
    [Fact]
    public async Task ASyncQueuedBehindARemovedLogin_ChangesNothing_AndStartsNoSession()
    {
        var (_, member) = await App.AddMemberAsync(_owner, "dana@skanyxx.example", SkanyxxRoles.Employee);
        await BindAsync(member);

        var synced = await RaceAsync(await NormalizedEmailAsync(member), AccountLock.LockSeed,
            accounts => accounts.SyncAsync(member, Key, Supervisor, CancellationToken.None), UnbindAsync);

        Assert.Null(synced);
        Assert.Equal([SkanyxxRoles.Employee], await RolesOfAsync(member));
    }

    /// <summary>CR m1: a refusal queued behind the removal leaves the now local-only account alone: roles kept, no revocation owed.</summary>
    [Fact]
    public async Task ARefusalQueuedBehindARemovedLogin_ChangesNothing()
    {
        var (_, member) = await App.AddMemberAsync(_owner, "dana@skanyxx.example", SkanyxxRoles.Supervisor);
        await BindAsync(member);

        await RaceAsync(await NormalizedEmailAsync(member), AccountLock.LockSeed, async accounts =>
        {
            await accounts.RefuseAsync(member, Key, "no mapped group", CancellationToken.None);
            return true;
        }, UnbindAsync);

        Assert.Equal([SkanyxxRoles.Supervisor], await RolesOfAsync(member));
        await using var db = Postgres.CreateDbContext();
        Assert.False(await db.PendingRevocations.AnyAsync(p => p.UserId == member));
    }

    /// <summary>CR m2: while another check holds the account, a link's password step-up is refused as busy (429), unchecked and uncounted.</summary>
    [Fact]
    public async Task AStepUpFindingTheAccountBusy_Is429_UncheckedAndUncounted()
    {
        Assert.Equal(HttpStatusCode.OK, (await App.Client(bearer: _owner).PutAsJsonAsync("/api/identity/entra/settings", EntraSettingsTests.Settings())).StatusCode);
        var (_, member) = await App.AddMemberAsync(_owner, "dana@skanyxx.example");
        await using var holder = new NpgsqlConnection(Postgres.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await ExecuteAsync(holder, "SELECT pg_advisory_xact_lock(hashtextextended(@key, @seed))", await NormalizedEmailAsync(member), AccountLock.LockSeed);

        await using var scope = App.Services.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new EntraChallengeQuery("/Account?handler=Linked", member, "a wrong password"));
        await transaction.CommitAsync();

        Assert.Equal(OutcomeStatus.RateLimited, outcome.Status);
        Assert.Equal(PasswordStepUp.Busy, outcome.Message);
        await using var db = Postgres.CreateDbContext();
        Assert.Equal(0, await db.Users.Where(u => u.Id == member).Select(u => u.AccessFailedCount).SingleAsync());
    }

    /// <summary>
    /// Holds the advisory lock (<paramref name="text"/>, <paramref name="seed"/>), starts <paramref name="call"/>, and
    /// runs <paramref name="whileHeld"/> only once the call is seen waiting on that lock; then lets go.
    /// </summary>
    private async Task<T> RaceAsync<T>(string text, long seed, Func<EntraAccounts, Task<T>> call, Func<NpgsqlConnection, Task> whileHeld)
    {
        await using var holder = new NpgsqlConnection(Postgres.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await ExecuteAsync(holder, "SELECT pg_advisory_xact_lock(hashtextextended(@key, @seed))", text, seed);

        await using var scope = App.Services.CreateAsyncScope();
        var racing = call(scope.ServiceProvider.GetRequiredService<EntraAccounts>());
        var queued = false;
        for (var waited = 0; waited < 200 && !racing.IsCompleted && !(queued = await WaitsForAsync(text, seed)); waited++)
            await Task.Delay(50);
        Assert.True(queued, "The app's call was never seen waiting for this lock.");

        await whileHeld(holder);
        await transaction.CommitAsync();
        return await racing;
    }

    /// <summary>Someone waits for exactly this 64-bit advisory lock: its high and low halves are pg_locks' classid and objid.</summary>
    private async Task<bool> WaitsForAsync(string text, long seed)
    {
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_locks, (SELECT hashtextextended(@key, @seed) AS k) wanted
            WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1
              AND classid = ((wanted.k >> 32) & 4294967295)::oid AND objid = (wanted.k & 4294967295)::oid
            """, connection);
        command.Parameters.AddWithValue("key", text);
        command.Parameters.AddWithValue("seed", seed);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    private async Task BindAsync(string userId)
    {
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await BindAsync(connection, userId);
    }

    private static Task BindAsync(NpgsqlConnection connection, string userId) => ExecuteAsync(connection,
        """INSERT INTO identity_user_logins ("LoginProvider", "ProviderKey", "ProviderDisplayName", "UserId") VALUES ('entra', @key, 'Microsoft', @id)""",
        Key, id: userId);

    private static Task UnbindAsync(NpgsqlConnection holder) =>
        ExecuteAsync(holder, """DELETE FROM identity_user_logins WHERE "LoginProvider" = 'entra' AND "ProviderKey" = @key""", Key);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, string key, long? seed = null, string? id = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", key);
        if (seed is not null)
            command.Parameters.AddWithValue("seed", seed.Value);
        if (id is not null)
            command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> NormalizedEmailAsync(string userId)
    {
        await using var db = Postgres.CreateDbContext();
        return (await db.Users.Where(u => u.Id == userId).Select(u => u.NormalizedEmail).SingleAsync())!;
    }

    private async Task<List<string>> RolesOfAsync(string userId)
    {
        await using var db = Postgres.CreateDbContext();
        return await db.UserRoles.Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync();
    }

    private async Task<List<string>> LoginsOfAsync(string userId)
    {
        await using var db = Postgres.CreateDbContext();
        return [.. db.UserLogins.Where(l => l.UserId == userId).Select(l => l.ProviderKey)];
    }
}
