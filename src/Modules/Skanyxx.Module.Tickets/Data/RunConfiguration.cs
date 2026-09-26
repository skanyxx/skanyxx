using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Data;

internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> b)
    {
        b.ToTable("ticket_runs");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.TicketKey).HasColumnName("ticket_key").HasMaxLength(64);
        b.Property(r => r.Ticket).HasColumnName("ticket").HasJson();
        b.Property(r => r.PipelineId).HasColumnName("pipeline_id").HasMaxLength(64);
        b.Property(r => r.PipelineName).HasColumnName("pipeline_name").HasMaxLength(200);
        b.Property(r => r.Stages).HasColumnName("stages").HasJson();
        b.Property(r => r.State).HasColumnName("state").AsName();
        b.Property(r => r.Cursor).HasColumnName("cursor");
        b.Property(r => r.Loops).HasColumnName("loops").HasJson();
        b.Property(r => r.LoopedFrom).HasColumnName("looped_from").HasJson();
        b.Property(r => r.Error).HasColumnName("error").HasMaxLength(500);
        b.Property(r => r.CreatedBy).HasColumnName("created_by").HasMaxLength(128);
        b.Property(r => r.CreatedAt).HasColumnName("created_at");
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at");
        // A person deciding a gate or cancelling races the worker stepping the same run; the loser re-reads.
        b.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken();
        b.Ignore(r => r.CurrentStage);
        b.Ignore(r => r.IsTerminal);
        b.HasMany(r => r.StageRuns).WithOne().HasForeignKey(s => s.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => r.State);
        b.HasIndex(r => r.TicketKey);
    }
}
