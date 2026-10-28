using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> card)
    {
        card.ToTable("memory_cards", t =>
        {
            t.HasCheckConstraint("ck_memory_cards_scope",
                "scope = 'company' OR scope ~ '^(personal|team|department):[a-z0-9][a-z0-9._@-]{0,127}$'");
            t.HasCheckConstraint("ck_memory_cards_key", "key ~ '^[a-z0-9][a-z0-9-]{0,79}$'");
            t.HasCheckConstraint("ck_memory_cards_type", "type IN ('decision', 'fact', 'procedure', 'open')");
            t.HasCheckConstraint("ck_memory_cards_status", "status IN ('candidate', 'published', 'stale')");
            t.HasCheckConstraint("ck_memory_cards_version", "version > 0");
            t.HasCheckConstraint("ck_memory_cards_origin", "origin IN ('manual', 'chat', 'run')");
            t.HasCheckConstraint("ck_memory_cards_tickets", $"cardinality(tickets) <= {CardLimits.Tickets}");
            t.HasCheckConstraint("ck_memory_cards_links", $"cardinality(links) <= {CardLimits.Links}");
            t.HasCheckConstraint("ck_memory_cards_keywords", $"cardinality(keywords) <= {CardLimits.Keywords}");
        });

        card.Property(c => c.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        card.Property(c => c.Scope).HasColumnName("scope").HasMaxLength(160);
        card.Property(c => c.Key).HasColumnName("key").HasMaxLength(80);
        // D041: an UPDATE only lands if the row still has the version the writer read.
        card.Property(c => c.Version).HasColumnName("version").IsConcurrencyToken();
        card.Property(c => c.Type).HasColumnName("type").HasMaxLength(16).HasConversion(Lower<CardType>());
        card.Property(c => c.What).HasColumnName("what").HasMaxLength(CardLimits.What);
        card.Property(c => c.Why).HasColumnName("why").HasMaxLength(CardLimits.Why);
        card.Property(c => c.Who).HasColumnName("who").HasMaxLength(300);
        card.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        // Rows from before the column existed read as created when they were last updated (set in the migration).
        card.Property(c => c.CreatedAt).HasColumnName("created_at");
        card.Property(c => c.Status).HasColumnName("status").HasMaxLength(16).HasConversion(Lower<CardStatus>());
        card.Property(c => c.Body).HasColumnName("body").HasMaxLength(CardLimits.Body);
        card.Property(c => c.Source).HasColumnName("source").HasMaxLength(CardLimits.Source);
        card.Property(c => c.LiftedFromId).HasColumnName("lifted_from");
        card.Property(c => c.Tickets).HasColumnName("tickets");
        card.Property(c => c.Links).HasColumnName("links");
        card.Property(c => c.Keywords).HasColumnName("keywords");
        card.Property(c => c.Origin).HasColumnName("origin").HasMaxLength(16).HasConversion(Lower<CardOrigin>());
        card.Property(c => c.AgentId).HasColumnName("agent_id").HasMaxLength(128);
        card.Property(c => c.SupersededBy).HasColumnName("superseded_by").HasMaxLength(CardLimits.LinkLength);
        card.Property(c => c.Search).HasColumnName("search").HasComputedColumnSql(
            // The slug itself (so a search for 'refund-window' finds it) and its words, stemmed.
            "setweight(to_tsvector('english'::regconfig, key), 'A') || " +
            "setweight(to_tsvector('english'::regconfig, replace(key, '-', ' ')), 'A') || " +
            "setweight(to_tsvector('english'::regconfig, what), 'A') || " +
            "setweight(to_tsvector('english'::regconfig, why), 'B')",
            stored: true);

        card.HasKey(c => c.Id);
        card.HasIndex(c => new { c.Scope, c.Key }).IsUnique();
        card.HasIndex(c => c.Search).HasMethod("GIN");
        card.HasIndex(c => c.Tickets).HasMethod("GIN");
        card.HasIndex(c => c.Keywords).HasMethod("GIN");
        card.HasOne<Card>().WithMany().HasForeignKey(c => c.LiftedFromId).OnDelete(DeleteBehavior.SetNull);
    }

    internal static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string> Lower<T>() where T : struct, Enum =>
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<T>(v, true));
}
