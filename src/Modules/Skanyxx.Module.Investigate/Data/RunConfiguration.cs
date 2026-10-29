using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Data;

internal sealed class RunConfiguration : IEntityTypeConfiguration<InvestigationRun>
{
    public void Configure(EntityTypeBuilder<InvestigationRun> b)
    {
        b.ToTable("investigate_runs");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.WorkflowId).HasColumnName("workflow_id");
        b.Property(r => r.WorkflowName).HasColumnName("workflow_name").HasMaxLength(120);
        b.Property(r => r.Steps).HasColumnName("steps").HasJson();
        b.Property(r => r.Input).HasColumnName("input");
        b.Property(r => r.State).HasColumnName("state").AsName();
        b.Property(r => r.Failure).HasColumnName("failure").HasMaxLength(1000);
        b.Property(r => r.CreatedBy).HasColumnName("created_by").HasMaxLength(128);
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.Property(r => r.StartedAt).HasColumnName("started_at");
        b.Property(r => r.EndedAt).HasColumnName("ended_at");
        b.Property(r => r.HeartbeatAt).HasColumnName("heartbeat_at");
        b.Property(r => r.CancelRequested).HasColumnName("cancel_requested");
        b.Property(r => r.CardScope).HasColumnName("card_scope").HasMaxLength(200);
        b.Property(r => r.CardKey).HasColumnName("card_key").HasMaxLength(200);
        b.Ignore(r => r.IsFinished);
        b.HasMany(r => r.StepRuns).WithOne().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.State);
        b.HasIndex(r => new { r.CreatedBy, r.CreatedAt });
        b.HasIndex(r => r.WorkflowId);
    }
}
