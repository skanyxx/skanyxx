using Npgsql;
using Testcontainers.PostgreSql;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>One Postgres for the collection and one Host on it with the default (production-like) settings.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public HostApp Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Host = await HostApp.StartAsync(ConnectionString);
    }

    /// <summary>A new empty database in the same server (no owner yet), for hosts that must start un-bootstrapped.</summary>
    public async Task<string> NewDatabaseAsync()
    {
        var name = "skanyxx_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>Freezes the database (like a stalled server): connections open but nothing answers.</summary>
    public async Task WhilePausedAsync(Func<Task> action)
    {
        await _container.PauseAsync();
        try
        {
            await action();
        }
        finally
        {
            await _container.UnpauseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await _container.DisposeAsync();
    }
}
