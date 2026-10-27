using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.ChatContext.Domain;

namespace Skanyxx.Module.ChatContext.Data;

/// <summary>
/// <c>search</c> is a stored generated tsvector (title A, keywords/categories/tickets B, outcome C) with a GIN index, as
/// memory_cards does. <c>search_terms</c> is the space-joined keywords, categories and ticket keys: array_to_string is
/// not immutable, so the generated column reads this plain copy. A pgvector <c>embedding</c> column can join later
/// beside <c>search</c> without changing either (D236).
/// </summary>
internal sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> b)
    {
        b.ToTable("chat_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("session_id").HasMaxLength(128);
        b.Property(s => s.UserId).HasColumnName("user_id").HasMaxLength(128);
        b.Property(s => s.AgentRef).HasColumnName("agent_ref").HasMaxLength(127);
        b.Property(s => s.Title).HasColumnName("title").HasMaxLength(ChatSession.MaxTitle);
        b.Property(s => s.Scope).HasColumnName("scope").HasMaxLength(160);
        b.Property(s => s.Keywords).HasColumnName("keywords");
        b.Property(s => s.KeywordWeights).HasColumnName("keyword_weights").HasJson();
        b.Property(s => s.Categories).HasColumnName("categories");
        b.Property(s => s.TicketKeys).HasColumnName("ticket_keys");
        b.Property(s => s.Outcome).HasColumnName("outcome").HasMaxLength(ChatSession.MaxOutcome);
        b.Property(s => s.Turns).HasColumnName("turns");
        b.Property(s => s.CreatedAt).HasColumnName("created_at");
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        b.Property(s => s.SearchTerms).HasColumnName("search_terms");
        b.Property<NpgsqlTypes.NpgsqlTsVector>("Search").HasColumnName("search")
            .HasComputedColumnSql(
                "setweight(to_tsvector('english', title), 'A') || setweight(to_tsvector('english', search_terms), 'B') || " +
                "setweight(to_tsvector('english', outcome), 'C')", stored: true);

        b.HasIndex("Search").HasMethod("GIN");
        b.HasIndex(s => s.TicketKeys).HasMethod("GIN");
        b.HasIndex(s => new { s.UserId, s.UpdatedAt });
        b.HasIndex(s => s.Scope);
    }
}
