using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Data;

internal sealed class StepRunConfiguration : IEntityTypeConfiguration<StepRun>
{
    public void Configure(EntityTypeBuilder<StepRun> b)
    {
        b.ToTable("investigate_step_runs");
        b.HasKey(s => new { s.RunId, s.StepId });
        b.Property(s => s.RunId).HasColumnName("run_id");
        b.Property(s => s.StepId).HasColumnName("step_id").HasMaxLength(32);
        b.Property(s => s.State).HasColumnName("state").AsName();
        b.Property(s => s.StartedAt).HasColumnName("started_at");
        b.Property(s => s.EndedAt).HasColumnName("ended_at");
        b.Property(s => s.Output).HasColumnName("output");
        b.Property(s => s.Error).HasColumnName("error").HasMaxLength(1000);
    }
}
