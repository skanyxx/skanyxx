using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class RemovePluginServerHandler(MemoryDbContext db, ILogger<RemovePluginServerHandler> logger) : IRequestHandler<RemovePluginServerCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(RemovePluginServerCommand command, CancellationToken ct)
    {
        var removed = await db.StudioPluginServers
            .Where(s => s.Server == command.Server && (!command.UnclaimedOnly || !s.Claimed) && (command.Plugin == null || s.Plugin == command.Plugin)
                && (command.UpdatedBefore == null || s.UpdatedAt < command.UpdatedBefore))
            .ExecuteDeleteAsync(ct) > 0;
        if (removed)
            logger.LogWarning("Plugin server {Server}: record and stored header values deleted by {Actor}", command.Server, command.Actor);
        return Outcome<bool>.Ok(removed);
    }
}
