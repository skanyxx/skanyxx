using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Module.Memory.Data;

/// <summary>
/// Applies migrations at startup as a hosted service: an unreachable database stops the host
/// (fail closed) instead of being logged and swallowed like a module's InitializeAsync error.
/// Runs before Kestrel starts listening. Replicas starting together take turns under a session advisory lock.
/// </summary>
internal sealed class MemoryMigrator(IServiceScopeFactory scopes) : IHostedService
{
    // Every Postgres advisory lock key in Skanyxx. Keep them distinct (a one-bigint key never meets a two-int key):
    //   0x4D454D02                     memory migrator (this)                pg_advisory_lock(bigint)
    //   (0x4D454D01, hashtext(agent))  memory grant writes, per agent        pg_advisory_xact_lock(int, int)
    //   0x544B5402                     tickets run-worker leader             pg_advisory_lock(bigint)
    //   0x544B5403                     tickets migrator                      pg_advisory_lock(bigint)
    //   (0x544B5401, 0)                tickets run start (active-run caps)   pg_advisory_xact_lock(int, int)
    internal const long MigrateLockKey = 0x4D454D02;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
        // One open connection for the lock and the migration: the lock belongs to that session.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrateLockKey})", cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            // Explicit: a pooled connection keeps its session, and so the lock, after it is closed.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrateLockKey})", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
