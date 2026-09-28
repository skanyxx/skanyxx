using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Serializes everything that reads and writes one account's lockout state (password checks, unlock). Identity's
/// failed-count update is read-modify-write under optimistic concurrency and drops the losing writes, so parallel
/// wrong passwords would otherwise count as one failure. Keyed by normalized email, so the lock is taken before the
/// lookup and an unknown email behaves the same way. Must run inside a transaction.
/// </summary>
internal static class AccountLock
{
    // The seed of a 64-bit key. A 32-bit hashtext key let an attacker compute an unknown email that collides with the
    // victim's and keep the victim's lock busy without touching the account (SEC3 R3-1); 64 bits puts that at ~2^64.
    // Listed with every other advisory-lock key in Skanyxx.Module.Memory's MemoryMigrator.
    private const long LockSeed = 0x49444E03;

    /// <summary>Waits for the lock. Only for callers that proved the bootstrap token, so nobody can queue on it at will.</summary>
    public static Task AcquireAsync(AccountsDbContext db, string normalizedEmail, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({normalizedEmail}, {LockSeed}))", ct);

    /// <summary>
    /// False at once when another attempt holds the lock. Waiting would pin a pooled connection per queued guess, and
    /// a flood on one email would then starve every identity call (SEC2-N1).
    /// </summary>
    public static Task<bool> TryAcquireAsync(AccountsDbContext db, string normalizedEmail, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({normalizedEmail}, {LockSeed})) AS \"Value\"")
            .SingleAsync(ct);
}
