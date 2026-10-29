using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.Investigate.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class InvestigateDbContextFactory : IDesignTimeDbContextFactory<InvestigateDbContext>
{
    public InvestigateDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InvestigateDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(InvestigateDbContext.MigrationsTable))
            .Options);
}
