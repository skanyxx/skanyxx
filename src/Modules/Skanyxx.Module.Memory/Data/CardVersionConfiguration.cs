using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class CardVersionConfiguration : IEntityTypeConfiguration<CardVersion>
{
    public void Configure(EntityTypeBuilder<CardVersion> version)
    {
        version.ToTable("memory_card_versions");
        version.Property(v => v.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        version.Property(v => v.CardId).HasColumnName("card_id");
        version.Property(v => v.Version).HasColumnName("version");
        version.Property(v => v.Scope).HasColumnName("scope").HasMaxLength(160);
        version.Property(v => v.Key).HasColumnName("key").HasMaxLength(80);
        version.Property(v => v.Type).HasColumnName("type").HasMaxLength(16).HasConversion(CardConfiguration.Lower<CardType>());
        version.Property(v => v.What).HasColumnName("what").HasMaxLength(CardLimits.What);
        version.Property(v => v.Why).HasColumnName("why").HasMaxLength(CardLimits.Why);
        version.Property(v => v.Who).HasColumnName("who").HasMaxLength(300);
        version.Property(v => v.Status).HasColumnName("status").HasMaxLength(16).HasConversion(CardConfiguration.Lower<CardStatus>());
        version.Property(v => v.Tickets).HasColumnName("tickets");
        version.Property(v => v.UpdatedAt).HasColumnName("updated_at");

        version.HasKey(v => v.Id);
        // A write replaces a version once; a second row for it would mean two writers both won.
        version.HasIndex(v => new { v.CardId, v.Version }).IsUnique();
        // The history goes with the card: a deleted card leaves no orphaned text behind.
        version.HasOne<Card>().WithMany().HasForeignKey(v => v.CardId).OnDelete(DeleteBehavior.Cascade);
    }
}
