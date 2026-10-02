using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Data;

internal sealed class PipelineConfiguration : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> b)
    {
        b.ToTable("ticket_pipelines");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id").HasMaxLength(64);
        b.Property(p => p.Name).HasColumnName("name").HasMaxLength(200);
        b.Property(p => p.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(p => p.Stages).HasColumnName("stages").HasJson();
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
    }
}
