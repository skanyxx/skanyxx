using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Module.ChatContext.Data;

/// <summary>Migrates before Kestrel listens, one replica at a time, like memory's and tickets' migrators.</summary>
internal sealed class ChatContextMigrator(IServiceScopeFactory scopes) : IHostedService
{
    // All advisory-lock keys are listed in one place: Skanyxx.Module.Memory's MemoryMigrator.
    internal const long MigrateLockKey = 0x43485402;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatContextDbContext>();
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
