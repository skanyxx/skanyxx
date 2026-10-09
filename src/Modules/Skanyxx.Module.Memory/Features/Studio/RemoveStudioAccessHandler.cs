using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// Revokes a studio agent's secret and grants, and gives its name back — unless the owner suspended it (D121), which
/// stays recorded. A name the studio does not own is refused (D117); one memory knows nothing about is a no-op.
/// </summary>
internal sealed class RemoveStudioAccessHandler(MemoryDbContext db, ILogger<RemoveStudioAccessHandler> logger)
    : IRequestHandler<RemoveStudioAccessCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(RemoveStudioAccessCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (agent, refused) = await StudioAgentLock.AcquireAsync(db, command.AgentId, claim: false, ct);
        if (refused is not null)
            return Outcome<bool>.Forbidden(refused);
        if (agent is null)
            return Outcome<bool>.Ok(false);

        var secrets = await db.AgentSecrets.Where(s => s.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        var grants = await db.Grants.Where(g => g.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        if (agent.Suspended)
            agent.DeployedFingerprint = null;
        else
            db.StudioAgents.Remove(agent);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (secrets + grants > 0)
            logger.LogWarning("Agent memory access removed for {AgentId} by the studio for {Actor} (secret: {Secret}, grants: {Grants})",
                command.AgentId, command.Actor, secrets > 0, grants);
        return Outcome<bool>.Ok(secrets + grants > 0);
    }
}
