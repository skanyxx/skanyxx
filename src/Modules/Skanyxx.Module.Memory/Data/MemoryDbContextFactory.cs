using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skanyxx.Module.Memory.Data;

/// <summary>Design-time only (<c>dotnet ef migrations add</c>); never used at runtime.</summary>
internal sealed class MemoryDbContextFactory : IDesignTimeDbContextFactory<MemoryDbContext>
{
    public MemoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MemoryDbContext>()
            .UseNpgsql("Host=localhost;Database=skanyxx_design")
            .Options);
}
