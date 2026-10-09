using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class StudioRepoConfiguration : IEntityTypeConfiguration<StudioRepo>
{
    public void Configure(EntityTypeBuilder<StudioRepo> repo)
    {
        repo.ToTable("memory_studio_repo", t => t.HasCheckConstraint("ck_memory_studio_repo_single", "id = 1"));
        repo.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        repo.Property(r => r.RepoId).HasColumnName("repo_id");
        repo.Property(r => r.RepoCreatedAt).HasColumnName("repo_created_at").HasMaxLength(64);
        repo.Property(r => r.FullName).HasColumnName("full_name").HasMaxLength(256);
        repo.Property(r => r.RecordedAt).HasColumnName("recorded_at");
        repo.Property(r => r.RecordedBy).HasColumnName("recorded_by").HasMaxLength(128);
        repo.HasKey(r => r.Id);
    }
}
