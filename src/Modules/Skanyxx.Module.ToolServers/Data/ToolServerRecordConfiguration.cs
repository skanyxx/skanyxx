using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Skanyxx.Module.ToolServers.Data;

internal sealed class ToolServerRecordConfiguration : IEntityTypeConfiguration<ToolServerRecord>
{
    public void Configure(EntityTypeBuilder<ToolServerRecord> b)
    {
        b.ToTable("tool_servers");
        b.HasKey(s => new { s.Namespace, s.Name });
        b.Ignore(s => s.Ref);
        b.Property(s => s.Namespace).HasColumnName("namespace").HasMaxLength(63);
        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(63);
        b.Property(s => s.Url).HasColumnName("url").HasMaxLength(2048);
        b.Property(s => s.Protocol).HasColumnName("protocol").HasMaxLength(32);
        b.Property(s => s.TimeoutSeconds).HasColumnName("timeout_seconds");
        b.Property(s => s.Description).HasColumnName("description").HasMaxLength(500);
        b.Property(s => s.Headers).HasColumnName("headers").HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<ToolServerHeader>>(v, (JsonSerializerOptions?)null)!,
                new ValueComparer<List<ToolServerHeader>>((a, c) => a!.SequenceEqual(c!), v => v.Count, v => v.ToList()));
        b.Property(s => s.UpdatedBy).HasColumnName("updated_by").HasMaxLength(128);
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}
