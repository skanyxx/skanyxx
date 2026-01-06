using Microsoft.EntityFrameworkCore;

namespace SkanyxxWeb.Data;

public class 
    AppDbContext : DbContext
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

        // Settings - key-value pairs
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Value).HasMaxLength(4000);
        });

        // KAgent Connections
        modelBuilder.Entity<KAgentConnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.BaseUrl).HasMaxLength(500).IsRequired();
        });

        // Saved Layouts
        modelBuilder.Entity<SavedLayout>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LayoutJson).IsRequired();
        });

        // Seed default settings
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

        // Seed default connection
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

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class KAgentConnection
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public int Port { get; set; } = 8083;
    public string Protocol { get; set; } = "http";
    public string? Token { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SavedLayout
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
