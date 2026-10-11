using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class SandboxTokenConfiguration : IEntityTypeConfiguration<SandboxToken>
{
    public void Configure(EntityTypeBuilder<SandboxToken> token)
    {
        token.ToTable("memory_sandbox_tokens", t =>
        {
            t.HasCheckConstraint("ck_memory_sandbox_tokens_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
            t.HasCheckConstraint("ck_memory_sandbox_tokens_hash", "octet_length(token_hash) = 32");
        });
        // The primary key is what makes one live token per sandbox activation: issuing again overwrites the row.
        token.Property(t => t.AgentId).HasColumnName("agent_id").HasMaxLength(128);
        token.Property(t => t.SandboxName).HasColumnName("sandbox_name").HasMaxLength(128);
        token.Property(t => t.OwnerId).HasColumnName("owner_id").HasMaxLength(128);
        token.Property(t => t.TokenHash).HasColumnName("token_hash");
        token.Property(t => t.ExpiresAt).HasColumnName("expires_at");
        token.Property(t => t.CreatedAt).HasColumnName("created_at");
        token.HasKey(t => t.AgentId);
        token.HasIndex(t => t.TokenHash).IsUnique();
        token.HasIndex(t => t.OwnerId);
    }
}
