using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class AgentSecretConfiguration : IEntityTypeConfiguration<AgentSecret>
{
    public void Configure(EntityTypeBuilder<AgentSecret> secret)
    {
        secret.ToTable("memory_agent_secrets", t =>
        {
            t.HasCheckConstraint("ck_memory_agent_secrets_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
            t.HasCheckConstraint("ck_memory_agent_secrets_hash", "octet_length(secret_hash) = 32");
        });
        // The primary key is what makes one active secret per agent: issuing again overwrites the row.
        secret.Property(s => s.AgentId).HasColumnName("agent_id").HasMaxLength(128);
        secret.Property(s => s.SecretHash).HasColumnName("secret_hash");
        secret.Property(s => s.CreatedAt).HasColumnName("created_at");
        secret.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(128);
        secret.Property(s => s.ActsForUsers).HasColumnName("acts_for_users");
        secret.HasKey(s => s.AgentId);
        secret.HasIndex(s => s.SecretHash).IsUnique();
    }
}
