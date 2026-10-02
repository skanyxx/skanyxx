using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.Identity.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design", o => o.MigrationsHistoryTable(AccountsDbContext.MigrationsTable))
            .Options);
}
