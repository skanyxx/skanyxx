using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.Tickets.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class TicketsDbContextFactory : IDesignTimeDbContextFactory<TicketsDbContext>
{
    public TicketsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TicketsDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(TicketsDbContext.MigrationsTable))
            .Options);
}
