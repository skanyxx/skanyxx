using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>Values that cannot be decrypted (the key ring lost) count as none: the server waits for the owner to enter them again.</summary>
internal sealed class PluginServerValuesHandler(MemoryDbContext db, PluginServerValueProtector protector, ILogger<PluginServerValuesHandler> logger)
    : IRequestHandler<PluginServerValuesQuery, Outcome<PluginServerValues>>
{
    public async Task<Outcome<PluginServerValues>> Handle(PluginServerValuesQuery query, CancellationToken ct)
    {
        var row = await db.StudioPluginServers.AsNoTracking().Where(s => s.Server == query.Server)
            .Select(s => new { s.ProtectedValues, s.ValueFingerprint, s.ValueAddress }).FirstOrDefaultAsync(ct);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (row?.ProtectedValues is { } stored)
        {
            try
            {
                values = protector.Unprotect(stored);
            }
            catch (CryptographicException)
            {
                logger.LogError("The stored header values of plugin server {Server} cannot be decrypted (their Data Protection key is gone); "
                    + "the owner enters them again", query.Server);
            }
        }
        return Outcome<PluginServerValues>.Ok(new PluginServerValues(query.Server, values, row?.ValueFingerprint, row?.ValueAddress));
    }
}
