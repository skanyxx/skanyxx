using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>A record another plugin left unclaimed is taken over without its values; a claimed one of another plugin is refused.</summary>
internal sealed class ClaimPluginServerHandler(MemoryDbContext db) : IRequestHandler<ClaimPluginServerCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(ClaimPluginServerCommand command, CancellationToken ct)
    {
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memory_studio_plugin_servers (server, plugin, value_names, claimed, updated_at)
            VALUES ({command.Server}, {command.Plugin}, {Array.Empty<string>()}, true, {DateTime.UtcNow})
            ON CONFLICT (server) DO UPDATE
            SET claimed = true, updated_at = excluded.updated_at,
                plugin = excluded.plugin,
                protected_values = CASE WHEN memory_studio_plugin_servers.plugin = excluded.plugin THEN memory_studio_plugin_servers.protected_values END,
                value_names = CASE WHEN memory_studio_plugin_servers.plugin = excluded.plugin THEN memory_studio_plugin_servers.value_names ELSE excluded.value_names END,
                value_fingerprint = CASE WHEN memory_studio_plugin_servers.plugin = excluded.plugin THEN memory_studio_plugin_servers.value_fingerprint END,
                value_address = CASE WHEN memory_studio_plugin_servers.plugin = excluded.plugin THEN memory_studio_plugin_servers.value_address END
            WHERE memory_studio_plugin_servers.plugin = excluded.plugin OR NOT memory_studio_plugin_servers.claimed
            """, ct);
        return claimed == 1
            ? Outcome<bool>.Ok(true)
            : Outcome<bool>.Conflict(false, $"The server '{command.Server}' is claimed for another plugin.", "taken");
    }
}
