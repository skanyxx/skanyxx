using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class PluginServerRecordsHandler(MemoryDbContext db) : IRequestHandler<PluginServerRecordsQuery, Outcome<IReadOnlyList<PluginServerRecord>>>
{
    public async Task<Outcome<IReadOnlyList<PluginServerRecord>>> Handle(PluginServerRecordsQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyList<PluginServerRecord>>.Ok(await db.StudioPluginServers.AsNoTracking().OrderBy(s => s.Server)
            .Select(s => new PluginServerRecord(s.Server, s.Plugin, s.ValueNames, s.ValueFingerprint, s.DeployedFingerprint, s.DeployedSpecHash, s.Claimed, s.ValueAddress, s.UpdatedAt))
            .ToListAsync(ct));
}
