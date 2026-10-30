using Microsoft.EntityFrameworkCore;

namespace Skanyxx.Module.Quotas.Data;

public sealed class QuotaDbContext(DbContextOptions<QuotaDbContext> options) : DbContext(options)
{
    /// <summary>Its own history table: quotas share memory's database, not memory's migrations.</summary>
    public const string MigrationsTable = "__quotas_migrations";

    public DbSet<QuotaLimit> Limits => Set<QuotaLimit>();
    public DbSet<QuotaUsage> Usage => Set<QuotaUsage>();
    public DbSet<QuotaRequest> Requests => Set<QuotaRequest>();
    public DbSet<QuotaPrice> Prices => Set<QuotaPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuotaLimit>(limit =>
        {
            limit.ToTable("quota_limits");
            limit.HasKey(l => new { l.Kind, l.ScopeId });
            limit.Property(l => l.Kind).HasColumnName("kind").HasMaxLength(16);
            limit.Property(l => l.ScopeId).HasColumnName("scope_id").HasMaxLength(450);
            limit.Property(l => l.MonthlyTokens).HasColumnName("monthly_tokens");
            limit.Property(l => l.SetBy).HasColumnName("set_by").HasMaxLength(450);
            limit.Property(l => l.SetAt).HasColumnName("set_at");
        });
        modelBuilder.Entity<QuotaUsage>(usage =>
        {
            usage.ToTable("quota_usage");
            usage.HasKey(u => new { u.Month, u.UserId, u.Model });
            usage.Property(u => u.Month).HasColumnName("month");
            usage.Property(u => u.UserId).HasColumnName("user_id").HasMaxLength(450);
            usage.Property(u => u.Model).HasColumnName("model").HasMaxLength(320);
            usage.Property(u => u.PromptTokens).HasColumnName("prompt_tokens");
            usage.Property(u => u.CompletionTokens).HasColumnName("completion_tokens");
            usage.Property(u => u.EstimatedTokens).HasColumnName("estimated_tokens");
            usage.Property(u => u.EstimatedCompletionTokens).HasColumnName("estimated_completion_tokens");
            usage.Property(u => u.ClientTokens).HasColumnName("client_tokens");
            usage.Property(u => u.Turns).HasColumnName("turns");
            usage.Property(u => u.EstimatedTurns).HasColumnName("estimated_turns");
        });
        modelBuilder.Entity<QuotaRequest>(request =>
        {
            request.ToTable("quota_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            request.Property(r => r.UserId).HasColumnName("user_id").HasMaxLength(450);
            request.Property(r => r.Amount).HasColumnName("amount");
            request.Property(r => r.Reason).HasColumnName("reason").HasMaxLength(500);
            request.Property(r => r.Status).HasColumnName("status").HasMaxLength(16);
            request.Property(r => r.CreatedAt).HasColumnName("created_at");
            request.Property(r => r.DecidedBy).HasColumnName("decided_by").HasMaxLength(450);
            request.Property(r => r.DecidedAt).HasColumnName("decided_at");
            request.Property(r => r.Month).HasColumnName("month");
            // One open request per person, enforced by the database so two quick submits cannot both land.
            request.HasIndex(r => r.UserId).IsUnique().HasFilter("status = 'pending'").HasDatabaseName("ux_quota_requests_pending");
            request.HasIndex(r => new { r.Month, r.UserId });
        });
        modelBuilder.Entity<QuotaPrice>(price =>
        {
            price.ToTable("quota_prices");
            price.HasKey(p => p.Model);
            price.Property(p => p.Model).HasColumnName("model").HasMaxLength(320);
            price.Property(p => p.PricePer1K).HasColumnName("price_per_1k").HasPrecision(18, 6);
            price.Property(p => p.SetBy).HasColumnName("set_by").HasMaxLength(450);
            price.Property(p => p.SetAt).HasColumnName("set_at");
        });
    }
}
