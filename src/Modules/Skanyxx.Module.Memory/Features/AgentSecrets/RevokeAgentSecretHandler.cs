using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

internal sealed class RevokeAgentSecretHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<RevokeAgentSecretCommand, Outcome<string>>
{
    public async Task<Outcome<string>> Handle(RevokeAgentSecretCommand command, CancellationToken ct)
    {
        if (!access.IsSupervisor(command.Caller))
            return Outcome<string>.Forbidden("Only a supervisor may revoke agent secrets.");

        // D084: an owner's acts-for-users secret is the owner's to revoke; the condition is part of the one DELETE.
        var isOwner = access.IsOwner(command.Caller);
        var deleted = await db.AgentSecrets
            .Where(s => s.AgentId == command.AgentId && (isOwner || !s.ActsForUsers))
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
            return Outcome<string>.Ok(command.AgentId);

        return await db.AgentSecrets.AnyAsync(s => s.AgentId == command.AgentId && s.ActsForUsers, ct)
            ? Outcome<string>.Forbidden("Only the owner may revoke the secret of an agent that acts for users.")
            : Outcome<string>.NotFound($"Agent '{command.AgentId}' has no secret.");
    }
}
