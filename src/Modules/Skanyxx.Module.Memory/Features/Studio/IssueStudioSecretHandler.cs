using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// One active secret per agent, as the REST issue (D083): issuing again rotates. <c>created_by</c> is
/// <see cref="StudioSecret.IssuedBy"/>, so offboarding a person (D088) never cuts off an agent a supervisor merged, and
/// <c>acts_for_users</c> is always false: a studio agent has no user (D084). Only for a name the studio owns (D117),
/// never while the owner has it suspended (D121).
/// </summary>
internal sealed class IssueStudioSecretHandler(MemoryDbContext db, ILogger<IssueStudioSecretHandler> logger)
    : IRequestHandler<IssueStudioSecretCommand, Outcome<StudioSecret>>
{
    public async Task<Outcome<StudioSecret>> Handle(IssueStudioSecretCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (agent, refused) = await StudioAgentLock.AcquireAsync(db, command.AgentId, claim: true, ct);
        if (refused is not null)
            return Outcome<StudioSecret>.Forbidden(refused);
        if (agent!.Suspended)
            return Outcome<StudioSecret>.Forbidden(StudioAgentLock.Suspended);

        var secret = AgentSecretToken.Generate();
        var hash = AgentSecretToken.Hash(secret);
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memory_agent_secrets (agent_id, secret_hash, created_at, created_by, acts_for_users)
            VALUES ({command.AgentId}, {hash}, {now.AddTicks(-(now.Ticks % 10))}, {StudioSecret.IssuedBy}, false)
            ON CONFLICT (agent_id) DO UPDATE
            SET secret_hash = excluded.secret_hash, created_at = excluded.created_at, created_by = excluded.created_by,
                acts_for_users = false
            """, ct);
        await tx.CommitAsync(ct);
        var fingerprint = StudioAgentLock.Fingerprint(hash);
        logger.LogWarning("Agent memory secret {Fingerprint} issued for {AgentId} by the studio for {Actor}; acts for users: False",
            fingerprint, command.AgentId, command.Actor);
        return Outcome<StudioSecret>.Ok(new StudioSecret(command.AgentId, secret, fingerprint));
    }
}
