using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skanyxx.Module.Analytics.Data;

internal sealed class UsageDayConfiguration : IEntityTypeConfiguration<UsageDay>
{
    public const string Table = "analytics_usage_daily";

    public void Configure(EntityTypeBuilder<UsageDay> usage)
    {
        usage.ToTable(Table);
        // Day first: every report reads a day range, then narrows by person (the key's own index serves both).
        usage.HasKey(u => new { u.Day, u.UserId, u.Agent, u.Model });
        usage.Property(u => u.Day).HasColumnName("day");
        usage.Property(u => u.UserId).HasColumnName("user_id").HasMaxLength(450);
        usage.Property(u => u.Agent).HasColumnName("agent").HasMaxLength(320);
        usage.Property(u => u.Model).HasColumnName("model").HasMaxLength(320);
        usage.Property(u => u.Requests).HasColumnName("requests");
        usage.Property(u => u.Chats).HasColumnName("chats");
        usage.Property(u => u.TokensIn).HasColumnName("tokens_in");
        usage.Property(u => u.TokensOut).HasColumnName("tokens_out");
        usage.Property(u => u.ClientTokens).HasColumnName("client_tokens");
        usage.Property(u => u.Unreported).HasColumnName("unreported");
        usage.Property(u => u.CacheHits).HasColumnName("cache_hits");
        usage.Property(u => u.TokensSaved).HasColumnName("tokens_saved");
        usage.Property(u => u.Errors).HasColumnName("errors");
    }
}
