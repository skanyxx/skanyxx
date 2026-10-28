using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.Analytics.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(AnalyticsDbContext.MigrationsTable))
            .Options);
}
