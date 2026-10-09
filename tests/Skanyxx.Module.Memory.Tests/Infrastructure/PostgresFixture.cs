using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Data;
using Testcontainers.PostgreSql;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public MemoryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<MemoryDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE memory_cards, memory_agent_grants, memory_agent_secrets RESTART IDENTITY CASCADE");
    }

    public async Task<int> CardCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.Cards.CountAsync();
    }
}
