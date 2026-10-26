using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Data;

internal sealed class StudioPluginServerConfiguration : IEntityTypeConfiguration<StudioPluginServer>
{
    public void Configure(EntityTypeBuilder<StudioPluginServer> server)
    {
        server.ToTable("memory_studio_plugin_servers", t =>
        {
            t.HasCheckConstraint("ck_memory_studio_plugin_servers_server", "server ~ '^[a-z0-9]([-a-z0-9]*[a-z0-9])?$' AND length(server) <= 50");
            t.HasCheckConstraint("ck_memory_studio_plugin_servers_plugin", "plugin ~ '^[a-z]([-a-z0-9]*[a-z0-9])?$' AND length(plugin) <= 50");
        });
        server.Property(s => s.Server).HasColumnName("server").HasMaxLength(50);
        server.Property(s => s.Plugin).HasColumnName("plugin").HasMaxLength(50);
        server.Property(s => s.ProtectedValues).HasColumnName("protected_values");
        server.Property(s => s.ValueNames).HasColumnName("value_names");
        server.Property(s => s.ValueAddress).HasColumnName("value_address").HasMaxLength(2048);
        server.Property(s => s.ValueFingerprint).HasColumnName("value_fingerprint").HasMaxLength(64);
        server.Property(s => s.DeployedFingerprint).HasColumnName("deployed_fingerprint").HasMaxLength(64);
        server.Property(s => s.DeployedSpecHash).HasColumnName("deployed_spec_hash").HasMaxLength(64);
        server.Property(s => s.Claimed).HasColumnName("claimed");
        server.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        server.HasKey(s => s.Server);
    }
}
