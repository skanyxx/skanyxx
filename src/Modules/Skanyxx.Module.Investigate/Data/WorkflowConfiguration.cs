using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Data;

internal sealed class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> b)
    {
        b.ToTable("investigate_workflows");
        b.HasKey(w => w.Id);
        b.Property(w => w.Id).HasColumnName("id");
        b.Property(w => w.Name).HasColumnName("name").HasMaxLength(120);
        b.Property(w => w.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(w => w.OwnerId).HasColumnName("owner_id").HasMaxLength(128);
        b.Property(w => w.SharedTeam).HasColumnName("shared_team").HasMaxLength(64);
        b.Property(w => w.Steps).HasColumnName("steps").HasJson();
        b.Property(w => w.CreatedAt).HasColumnName("created_at");
        b.Property(w => w.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(w => w.OwnerId);
        b.HasIndex(w => w.SharedTeam);
    }
}
