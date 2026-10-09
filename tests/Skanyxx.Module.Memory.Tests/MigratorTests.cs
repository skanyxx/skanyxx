using Npgsql;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigratorTests(PostgresFixture postgres)
{
    // CR m2: replicas starting together take turns, as the tickets migrator does.
    [Fact]
    public async Task Startup_WaitsWhileAnotherReplicaHoldsTheMigrationLock()
    {
        await using var replica = new NpgsqlConnection(postgres.ConnectionString);
        await replica.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(replica, $"SELECT pg_advisory_lock({MemoryMigrator.MigrateLockKey})");
        await using var app = MemoryApp.Build(new Dictionary<string, string?> { ["ConnectionStrings:Memory"] = postgres.ConnectionString });

        var start = app.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        var waited = !start.IsCompleted;
        await ExecuteAsync(replica, $"SELECT pg_advisory_unlock({MemoryMigrator.MigrateLockKey})");
        await start.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(waited);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteScalarAsync();
    }
}
