using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Module.Investigate.Data;

/// <summary>
/// Migrates before Kestrel listens and before the worker reads runs, one replica at a time, like the other modules'
/// migrators.
/// </summary>
internal sealed class InvestigateMigrator(IDbContextFactory<InvestigateDbContext> dbs) : IHostedService
{
    // All advisory-lock keys are listed in one place: Skanyxx.Module.Memory's MemoryMigrator.
    internal const long MigrateLockKey = 0x494E5602;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbs.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrateLockKey})", cancellationToken);
            await db.GetService<IHistoryRepository>().CreateIfNotExistsAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            if (db.Database.GetDbConnection().State == ConnectionState.Open)
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrateLockKey})", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
