using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.Quotas.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class QuotaDbContextFactory : IDesignTimeDbContextFactory<QuotaDbContext>
{
    public QuotaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<QuotaDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(QuotaDbContext.MigrationsTable))
            .Options);
}
