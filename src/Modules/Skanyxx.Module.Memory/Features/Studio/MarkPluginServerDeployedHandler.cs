using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class MarkPluginServerDeployedHandler(MemoryDbContext db) : IRequestHandler<MarkPluginServerDeployedCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(MarkPluginServerDeployedCommand command, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memory_studio_plugin_servers (server, plugin, value_names, claimed, deployed_fingerprint, deployed_spec_hash, updated_at)
            VALUES ({command.Server}, {command.Plugin}, {Array.Empty<string>()}, true, {command.Fingerprint}, {command.SpecHash}, {DateTime.UtcNow})
            ON CONFLICT (server) DO UPDATE
            SET claimed = true, deployed_fingerprint = excluded.deployed_fingerprint,
                deployed_spec_hash = excluded.deployed_spec_hash, updated_at = excluded.updated_at
            WHERE memory_studio_plugin_servers.plugin = excluded.plugin
            """, ct) == 1
            ? Outcome<bool>.Ok(true)
            : Outcome<bool>.Conflict(false, $"The server '{command.Server}' is recorded for another plugin.", "taken");
}
