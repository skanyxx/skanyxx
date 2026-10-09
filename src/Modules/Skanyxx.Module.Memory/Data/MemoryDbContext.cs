using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

public sealed class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<AgentGrant> Grants => Set<AgentGrant>();
    public DbSet<AgentSecret> AgentSecrets => Set<AgentSecret>();
    public DbSet<StudioAgent> StudioAgents => Set<StudioAgent>();
    public DbSet<StudioRepo> StudioRepos => Set<StudioRepo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CardConfiguration());
        modelBuilder.ApplyConfiguration(new AgentGrantConfiguration());
        modelBuilder.ApplyConfiguration(new AgentSecretConfiguration());
        modelBuilder.ApplyConfiguration(new StudioAgentConfiguration());
        modelBuilder.ApplyConfiguration(new StudioRepoConfiguration());
    }
}
