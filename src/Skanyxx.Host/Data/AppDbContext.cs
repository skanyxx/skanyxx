using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Data;

namespace Skanyxx.Host.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<AppSetting> Settings { get; set; } = null!;
    public DbSet<KAgentConnection> Connections { get; set; } = null!;
    public DbSet<SavedLayout> Layouts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Value).HasMaxLength(4000);
        });

        modelBuilder.Entity<KAgentConnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.BaseUrl).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<SavedLayout>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LayoutJson).IsRequired();
        });

        modelBuilder.Entity<AppSetting>().HasData(
            new AppSetting { Key = "theme", Value = "dark" },
            new AppSetting { Key = "kagent.baseUrl", Value = "localhost" },
            new AppSetting { Key = "kagent.port", Value = "8083" },
            new AppSetting { Key = "kagent.protocol", Value = "http" },
            new AppSetting { Key = "notifications.enabled", Value = "true" },
            new AppSetting { Key = "notifications.sound", Value = "true" },
            new AppSetting { Key = "autoRefresh.enabled", Value = "true" },
            new AppSetting { Key = "autoRefresh.intervalSeconds", Value = "30" }
        );

        modelBuilder.Entity<KAgentConnection>().HasData(
            new KAgentConnection
            {
                Id = 1,
                Name = "Local KAgent",
                BaseUrl = "localhost",
                Port = 8083,
                Protocol = "http",
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            }
        );
    }
}
