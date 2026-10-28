using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.ToolServers.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class ToolServersDbContextFactory : IDesignTimeDbContextFactory<ToolServersDbContext>
{
    public ToolServersDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ToolServersDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(ToolServersDbContext.MigrationsTable))
            .Options);
}
