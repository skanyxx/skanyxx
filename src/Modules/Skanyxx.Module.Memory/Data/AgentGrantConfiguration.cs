using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class AgentGrantConfiguration : IEntityTypeConfiguration<AgentGrant>
{
    public void Configure(EntityTypeBuilder<AgentGrant> grant)
    {
        grant.ToTable("memory_agent_grants", t =>
        {
            t.HasCheckConstraint("ck_memory_agent_grants_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'");
            t.HasCheckConstraint("ck_memory_agent_grants_scope",
                "scope IN ('personal', 'company') OR scope ~ '^(team|department):[a-z0-9][a-z0-9._@-]{0,127}$'");
        });
        grant.Property(g => g.AgentId).HasColumnName("agent_id").HasMaxLength(128);
        grant.Property(g => g.Scope).HasColumnName("scope").HasMaxLength(160);
        grant.Property(g => g.CanSearch).HasColumnName("can_search");
        grant.Property(g => g.CanUpsert).HasColumnName("can_upsert");
        grant.HasKey(g => new { g.AgentId, g.Scope });
    }
}
