using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Data;

internal sealed class AgentTurnConfiguration : IEntityTypeConfiguration<AgentTurn>
{
    public void Configure(EntityTypeBuilder<AgentTurn> b)
    {
        b.ToTable("ticket_agent_turns");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        b.Property(t => t.StageRunId).HasColumnName("stage_run_id");
        b.Property(t => t.AgentId).HasColumnName("agent_id").HasMaxLength(64);
        b.Property(t => t.Agent).HasColumnName("agent").HasMaxLength(254);
        b.Property(t => t.Prompt).HasColumnName("prompt");
        b.Property(t => t.Output).HasColumnName("output");
        b.Property(t => t.Degraded).HasColumnName("degraded");
        b.Property(t => t.StartedAt).HasColumnName("started_at");
        b.Property(t => t.EndedAt).HasColumnName("ended_at");
    }
}
