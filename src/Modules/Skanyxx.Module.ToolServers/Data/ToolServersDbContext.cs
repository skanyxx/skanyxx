using Microsoft.EntityFrameworkCore;

namespace Skanyxx.Module.ToolServers.Data;

public sealed class ToolServersDbContext(DbContextOptions<ToolServersDbContext> options) : DbContext(options)
{
    /// <summary>Its own history table: the records share memory's database, not memory's migrations.</summary>
    public const string MigrationsTable = "__toolservers_migrations";

    public DbSet<ToolServerRecord> Servers => Set<ToolServerRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new ToolServerRecordConfiguration());
}
