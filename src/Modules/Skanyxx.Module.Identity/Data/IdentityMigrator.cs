using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// Applies migrations at startup before Kestrel listens; an unreachable database stops the host (fail closed).
/// Replicas starting together take turns under a session advisory lock. Runs in <see cref="StartingAsync"/>, before
/// any hosted service starts: Data Protection's key-ring service (registered by the Host before modules) reads keys
/// from this database on start, and on a fresh database the table would not exist yet.
/// </summary>
internal sealed class IdentityMigrator(IServiceScopeFactory scopes) : IHostedLifecycleService
{
    // All advisory-lock keys are listed in one place: Skanyxx.Module.Memory's MemoryMigrator. Identity adds:
    //   0x49444E02  identity migrator (this)          pg_advisory_lock(bigint)
    //   0x49444E01  owner bootstrap (one at a time)   pg_advisory_xact_lock(bigint)
    //   hashtextextended(normalized email, 0x49444E03)  password check, unlock, invite create/accept, role change, disable; per account   pg_advisory_xact_lock(bigint)
    internal const long MigrateLockKey = 0x49444E02;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        // One open connection for the lock and the migration: the lock belongs to that session.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrateLockKey})", cancellationToken);
            // Kept although EF 9+ locks the history table itself: Tickets seeds under it, and one pattern for all three.
            // Never wrap MigrateAsync in a user transaction: EF's own migration lock refuses to run inside one.
            // Create the history table first, so EF never reads a missing one (logged as an Error on a fresh database).
            await db.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            // Explicit: a pooled connection keeps its session, and so the lock, after it is closed. A broken
            // connection has lost its session and the lock with it, and unlocking there would throw over the real error.
            if (db.Database.GetDbConnection().State == ConnectionState.Open)
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrateLockKey})", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
