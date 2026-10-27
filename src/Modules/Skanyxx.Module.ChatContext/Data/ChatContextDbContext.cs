using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.ChatContext.Domain;

namespace Skanyxx.Module.ChatContext.Data;

public sealed class ChatContextDbContext(DbContextOptions<ChatContextDbContext> options) : DbContext(options)
{
    /// <summary>Its own history table: the index shares memory's database, not memory's migrations.</summary>
    public const string MigrationsTable = "__chatcontext_migrations";

    public DbSet<ChatSession> Sessions => Set<ChatSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new ChatSessionConfiguration());
}
