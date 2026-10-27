using Npgsql;
using Testcontainers.PostgreSql;

namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

/// <summary>One Postgres for the run; the app's own migrators (memory, chatcontext) build the schema on first start.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DO $$ BEGIN IF to_regclass('chat_sessions') IS NOT NULL THEN TRUNCATE chat_sessions; END IF; " +
            "IF to_regclass('memory_cards') IS NOT NULL THEN TRUNCATE memory_cards RESTART IDENTITY CASCADE; END IF; END $$", connection);
        await command.ExecuteNonQueryAsync();
    }
}
