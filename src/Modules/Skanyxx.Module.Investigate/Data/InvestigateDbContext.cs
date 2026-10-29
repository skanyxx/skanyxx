using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Data;

public sealed class InvestigateDbContext(DbContextOptions<InvestigateDbContext> options) : DbContext(options)
{
    /// <summary>Its own history table: the tables share memory's database, not memory's migrations.</summary>
    public const string MigrationsTable = "__investigate_migrations";

    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<InvestigationRun> Runs => Set<InvestigationRun>();
    public DbSet<StepRun> StepRuns => Set<StepRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowConfiguration());
        modelBuilder.ApplyConfiguration(new RunConfiguration());
        modelBuilder.ApplyConfiguration(new StepRunConfiguration());
    }
}
