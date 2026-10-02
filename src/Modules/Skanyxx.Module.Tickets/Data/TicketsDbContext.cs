using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Data;

public sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options)
{
    public const string MigrationsTable = "__tickets_migrations";

    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<StageRun> StageRuns => Set<StageRun>();
    public DbSet<AgentTurn> AgentTurns => Set<AgentTurn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PipelineConfiguration());
        modelBuilder.ApplyConfiguration(new RunConfiguration());
        modelBuilder.ApplyConfiguration(new StageRunConfiguration());
        modelBuilder.ApplyConfiguration(new AgentTurnConfiguration());
    }
}
