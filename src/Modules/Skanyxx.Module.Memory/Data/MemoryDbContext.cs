using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

public sealed class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<AgentGrant> Grants => Set<AgentGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CardConfiguration());
        modelBuilder.ApplyConfiguration(new AgentGrantConfiguration());
    }
}
