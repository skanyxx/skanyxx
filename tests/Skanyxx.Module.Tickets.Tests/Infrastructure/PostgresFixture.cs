using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Tickets.Data;
using Testcontainers.PostgreSql;

namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public TicketsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TicketsDbContext>()
            .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable(TicketsDbContext.MigrationsTable)).Options);

    /// <summary>Empties every table; the app's migrator re-seeds the default pipeline when it starts.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE ticket_agent_turns, ticket_stage_runs, ticket_runs, ticket_pipelines RESTART IDENTITY CASCADE");
    }
}
