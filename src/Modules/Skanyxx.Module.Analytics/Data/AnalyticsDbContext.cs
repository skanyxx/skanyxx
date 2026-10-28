using Microsoft.EntityFrameworkCore;

namespace Skanyxx.Module.Analytics.Data;

public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    /// <summary>Its own history table: the rollup shares memory's database, not memory's migrations.</summary>
    public const string MigrationsTable = "__analytics_migrations";

    public DbSet<UsageDay> UsageDays => Set<UsageDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new UsageDayConfiguration());
}
