using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// D121: suspending revokes the secret and the grants in the same transaction, so the agent is cut off from memory
/// before the reconciler even runs; resuming only lifts the flag (the next pass issues and grants again from git).
/// </summary>
internal sealed class SuspendStudioAgentHandler(MemoryDbContext db, ILogger<SuspendStudioAgentHandler> logger)
    : IRequestHandler<SuspendStudioAgentCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(SuspendStudioAgentCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (agent, refused) = await StudioAgentLock.AcquireAsync(db, command.AgentId, claim: command.Suspended, ct);
        if (refused is not null)
            return Outcome<bool>.Forbidden(refused);
        if (agent is null)
            return Outcome<bool>.NotFound($"The studio has no agent '{command.AgentId}' in memory.");

        var changed = agent.Suspended != command.Suspended;
        agent.Suspended = command.Suspended;
        if (command.Suspended)
        {
            agent.DeployedFingerprint = null;
            await db.AgentSecrets.Where(s => s.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
            await db.Grants.Where(g => g.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogWarning("Studio agent {AgentId} {Action} by {Actor}", command.AgentId, command.Suspended ? "suspended (secret and grants revoked)" : "resumed", command.Actor);
        return Outcome<bool>.Ok(changed);
    }
}
