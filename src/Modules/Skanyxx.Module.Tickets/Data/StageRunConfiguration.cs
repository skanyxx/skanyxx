using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Data;

internal sealed class StageRunConfiguration : IEntityTypeConfiguration<StageRun>
{
    public void Configure(EntityTypeBuilder<StageRun> b)
    {
        b.ToTable("ticket_stage_runs");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        b.Property(s => s.RunId).HasColumnName("run_id");
        b.Property(s => s.StageId).HasColumnName("stage_id").HasMaxLength(64);
        b.Property(s => s.Kind).HasColumnName("kind").AsName();
        b.Property(s => s.Title).HasColumnName("title").HasMaxLength(200);
        b.Property(s => s.Attempt).HasColumnName("attempt");
        b.Property(s => s.State).HasColumnName("state").AsName();
        b.Property(s => s.Verdict).HasColumnName("verdict").AsName();
        b.Property(s => s.Output).HasColumnName("output");
        b.Property(s => s.Degraded).HasColumnName("degraded");
        b.Property(s => s.Warnings).HasColumnName("warnings").HasJson();
        b.Property(s => s.StartedAt).HasColumnName("started_at");
        b.Property(s => s.EndedAt).HasColumnName("ended_at");
        b.Property(s => s.DecidedBy).HasColumnName("decided_by").HasMaxLength(128);
        b.Property(s => s.Note).HasColumnName("note").HasMaxLength(4000);
        b.HasMany(s => s.Turns).WithOne().HasForeignKey(t => t.StageRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(s => new { s.RunId, s.StageId, s.Attempt }).IsUnique();
    }
}
