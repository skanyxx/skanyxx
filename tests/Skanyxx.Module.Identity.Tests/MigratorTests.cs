using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigratorTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Startup_WaitsWhileAnotherReplicaHoldsTheMigrationLock()
    {
        await using var replica = new NpgsqlConnection(postgres.ConnectionString);
        await replica.OpenAsync();
        await ExecuteAsync(replica, $"SELECT pg_advisory_lock({IdentityMigrator.MigrateLockKey})");
        await using var app = IdentityApp.Build(new Dictionary<string, string?> { ["ConnectionStrings:Identity"] = postgres.ConnectionString });

        var start = app.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var waited = !start.IsCompleted;
        await ExecuteAsync(replica, $"SELECT pg_advisory_unlock({IdentityMigrator.MigrateLockKey})");
        await start.WaitAsync(TimeSpan.FromSeconds(30));
        await app.StopAsync();

        Assert.True(waited);
    }

    /// <summary>
    /// CR m4: the connection dies while waiting for the lock and the database refuses new connections. The unlock in
    /// <c>finally</c> must not replace the real error with its own failure to reconnect.
    /// </summary>
    [Fact]
    public async Task ABrokenConnection_SurfacesTheOriginalError()
    {
        var connectionString = await postgres.NewDatabaseAsync();
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        await using var admin = new NpgsqlConnection(postgres.ConnectionString);
        await admin.OpenAsync();
        await using var replica = new NpgsqlConnection(connectionString);
        await replica.OpenAsync();
        await ExecuteAsync(replica, $"SELECT pg_advisory_lock({IdentityMigrator.MigrateLockKey})");
        await using var app = IdentityApp.Build(new Dictionary<string, string?> { ["ConnectionStrings:Identity"] = connectionString });

        var start = app.StartAsync();
        var waiting = await WaitForLockWaiterAsync(admin, database);
        await ExecuteAsync(admin, $"ALTER DATABASE {database} ALLOW_CONNECTIONS false");
        await ExecuteAsync(admin, $"SELECT pg_terminate_backend({waiting})");
        var error = await Record.ExceptionAsync(() => start.WaitAsync(TimeSpan.FromSeconds(30)));
        await ExecuteAsync(admin, $"ALTER DATABASE {database} ALLOW_CONNECTIONS true");

        Assert.Equal(PostgresErrorCodes.AdminShutdown, Assert.IsType<PostgresException>(error).SqlState);
    }

    [Fact]
    public async Task EveryRole_IsSeeded()
    {
        await using var db = postgres.CreateDbContext();

        var roles = await db.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync();

        Assert.Equal(new[] { SkanyxxRoles.Builder, SkanyxxRoles.Employee, SkanyxxRoles.Owner, SkanyxxRoles.Supervisor }, roles);
    }

    [Fact]
    public async Task OwnHistoryTable_AndPrefixedTables()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name", connection);
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

        Assert.Contains(AccountsDbContext.MigrationsTable, tables);
        Assert.DoesNotContain("__EFMigrationsHistory", tables);
        Assert.All(tables.Where(t => t != AccountsDbContext.MigrationsTable), t => Assert.StartsWith("identity_", t));
    }

    /// <summary>A model change without a migration would pass every other test and fail the first insert in production.</summary>
    [Fact]
    public void TheModel_MatchesTheMigrations()
    {
        using var db = postgres.CreateDbContext();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var finalized = db.GetService<IModelRuntimeInitializer>().Initialize(((IMutableModel)snapshot).FinalizeModel());

        Assert.False(db.GetService<IMigrationsModelDiffer>().HasDifferences(
            finalized.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
    }

    private static async Task<int> WaitForLockWaiterAsync(NpgsqlConnection admin, string database)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT pid FROM pg_stat_activity WHERE datname = @db AND wait_event_type = 'Lock' AND query LIKE '%pg_advisory_lock%'", admin);
            command.Parameters.AddWithValue("db", database);
            if (await command.ExecuteScalarAsync() is int pid)
                return pid;
            await Task.Delay(100);
        }
        throw new TimeoutException("The migrator never waited on the lock.");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteScalarAsync();
    }
}
