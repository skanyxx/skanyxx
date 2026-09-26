using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Skanyxx.Module.Tickets.Engine;

namespace Skanyxx.Module.Tickets.Data;

/// <summary>
/// Migrates and seeds the default pipeline before Kestrel listens; an unreachable database stops the host.
/// Registered before the run worker, so the worker never sees an unmigrated schema. Replicas starting together
/// take turns under a session advisory lock, so two cannot both migrate, or both seed and one fail on the key.
/// </summary>
internal sealed class TicketsMigrator(IServiceScopeFactory scopes, TimeProvider clock) : IHostedService
{
    // All advisory-lock keys are listed in one place: Skanyxx.Module.Memory's MemoryMigrator.
    internal const long MigrateLockKey = 0x544B5403;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        // One open connection for the lock, the migration and the seed: the lock belongs to that session.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrateLockKey})", cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            if (!await db.Pipelines.AnyAsync(p => p.Id == DefaultPipeline.Id, cancellationToken))
            {
                db.Pipelines.Add(DefaultPipeline.Create(clock.GetUtcNow().UtcDateTime));
                await db.SaveChangesAsync(cancellationToken);
            }
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
