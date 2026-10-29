using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Investigate.Data;
using Testcontainers.PostgreSql;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public InvestigateDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<InvestigateDbContext>()
            .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable(InvestigateDbContext.MigrationsTable)).Options);

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE investigate_step_runs, investigate_runs, investigate_workflows CASCADE");
    }
}
