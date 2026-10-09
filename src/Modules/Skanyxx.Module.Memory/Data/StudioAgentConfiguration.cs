using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class StudioAgentConfiguration : IEntityTypeConfiguration<StudioAgent>
{
    public void Configure(EntityTypeBuilder<StudioAgent> agent)
    {
        agent.ToTable("memory_studio_agents", t =>
            t.HasCheckConstraint("ck_memory_studio_agents_agent", "agent_id ~ '^[a-z0-9][a-z0-9._@-]{0,127}$'"));
        agent.Property(a => a.AgentId).HasColumnName("agent_id").HasMaxLength(128);
        agent.Property(a => a.ClaimedAt).HasColumnName("claimed_at");
        agent.Property(a => a.Suspended).HasColumnName("suspended");
        agent.Property(a => a.DeployedFingerprint).HasColumnName("deployed_fingerprint").HasMaxLength(64);
        agent.HasKey(a => a.AgentId);
    }
}
