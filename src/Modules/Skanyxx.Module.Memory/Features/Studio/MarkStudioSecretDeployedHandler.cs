using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

internal sealed class MarkStudioSecretDeployedHandler(MemoryDbContext db) : IRequestHandler<MarkStudioSecretDeployedCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(MarkStudioSecretDeployedCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (agent, refused) = await StudioAgentLock.AcquireAsync(db, command.AgentId, claim: false, ct);
        if (refused is not null)
            return Outcome<bool>.Forbidden(refused);
        if (agent is null)
            return Outcome<bool>.NotFound($"The studio has no agent '{command.AgentId}' in memory.");
        agent.DeployedFingerprint = command.Fingerprint;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Outcome<bool>.Ok(true);
    }
}
