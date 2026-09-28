using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

internal sealed class IssueAgentSecretHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<IssueAgentSecretCommand, Outcome<IssuedAgentSecret>>
{
    public async Task<Outcome<IssuedAgentSecret>> Handle(IssueAgentSecretCommand command, CancellationToken ct)
    {
        if (!access.IsSupervisor(command.Caller))
            return Outcome<IssuedAgentSecret>.Forbidden("Only a supervisor may issue agent secrets.");
        var isOwner = access.IsOwner(command.Caller);
        // D084: an agent that acts for users can read and write any user's personal memory by naming them.
        if (command.ActsForUsers && !isOwner)
            return Outcome<IssuedAgentSecret>.Forbidden("Only the owner may let an agent act for users.");

        var secret = AgentSecretToken.Generate();
        var now = DateTime.UtcNow;
        var createdAt = now.AddTicks(-(now.Ticks % 10)); // Postgres keeps microseconds; answer what GET will read back
        // One statement, so two concurrent rotations cannot both insert: the last one wins and the other secret is dead.
        // The WHERE is checked against the row it locks, so a supervisor cannot replace an owner's acts-for-users secret
        // even if the owner flags it mid-request: the update is skipped and nothing is written.
        var written = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memory_agent_secrets (agent_id, secret_hash, created_at, created_by, acts_for_users)
            VALUES ({command.AgentId}, {AgentSecretToken.Hash(secret)}, {createdAt}, {command.Caller.UserId}, {command.ActsForUsers})
            ON CONFLICT (agent_id) DO UPDATE
            SET secret_hash = excluded.secret_hash, created_at = excluded.created_at, created_by = excluded.created_by,
                acts_for_users = excluded.acts_for_users
            WHERE {isOwner} OR NOT memory_agent_secrets.acts_for_users
            """, ct);
        if (written == 0)
            return Outcome<IssuedAgentSecret>.Forbidden("Only the owner may rotate the secret of an agent that acts for users.");

        return Outcome<IssuedAgentSecret>.Ok(new IssuedAgentSecret(command.AgentId, secret, createdAt, command.ActsForUsers));
    }
}
