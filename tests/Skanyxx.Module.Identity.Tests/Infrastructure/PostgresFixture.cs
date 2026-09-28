using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skanyxx.Module.Identity.Data;
using Testcontainers.PostgreSql;

namespace Skanyxx.Module.Identity.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AccountsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable(AccountsDbContext.MigrationsTable)).Options);

    /// <summary>Back to "not bootstrapped": every account goes, the seeded roles and the key ring stay.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE identity_users CASCADE");
    }

    /// <summary>A new empty database in the same server, for tests that must not disturb the shared one.</summary>
    public async Task<string> NewDatabaseAsync()
    {
        var name = "identity_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    public async Task<IdentityUser> OwnerAsync()
    {
        await using var db = CreateDbContext();
        return await db.Users.AsNoTracking().SingleAsync();
    }

    public async Task<int> UserCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.Users.CountAsync();
    }
}
