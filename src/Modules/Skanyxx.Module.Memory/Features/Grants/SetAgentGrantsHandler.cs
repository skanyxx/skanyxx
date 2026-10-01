using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

internal sealed class SetAgentGrantsHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<SetAgentGrantsCommand, Outcome<IReadOnlyList<GrantEntry>>>
{
    public async Task<Outcome<IReadOnlyList<GrantEntry>>> Handle(SetAgentGrantsCommand command, CancellationToken ct)
    {
        if (!access.IsSupervisor(command.Caller))
            return Outcome<IReadOnlyList<GrantEntry>>.Forbidden("Only a supervisor may change agent grants.");
        // D091: a team or department grant (search or upsert) opens that team to everyone the agent serves and lets it
        // write there in its own name; a supervisor writes only where they are a member, so only the owner sets these.
        var opensTeam = command.Grants.Any(g => AgentGrantLock.OpensTeamOrDepartment(g.Scope, g.CanSearch, g.CanUpsert));
        if (!access.IsOwner(command.Caller) && opensTeam)
            return Outcome<IReadOnlyList<GrantEntry>>.Forbidden("Only the owner may grant an agent a team or department scope.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Delete-then-insert is not atomic against a second writer for the same agent under READ COMMITTED
        // (grants would merge or hit the primary key); serialize writers per agent for this transaction.
        await AgentGrantLock.AcquireAsync(db, command.AgentId, ct);
        // Replacing is also removing: a supervisor must not strip the team grants the owner gave (checked under the lock).
        if (!access.IsOwner(command.Caller) && await AgentGrantLock.HoldsTeamOrDepartmentAsync(db, command.AgentId, ct))
            return Outcome<IReadOnlyList<GrantEntry>>.Forbidden("Only the owner may change the grants of an agent that holds a team or department scope.");
        // D092: a secret someone else issued would carry the team access to them; the owner rotates it first. Refused, not
        // revoked, so a running agent is never cut off as a side effect. Under the lock, so an issue cannot slip in.
        if (opensTeam && await db.AgentSecrets.AnyAsync(s => s.AgentId == command.AgentId && s.CreatedBy != command.Caller.UserId, ct))
            return Outcome<IReadOnlyList<GrantEntry>>.Conflict(null,
                "Rotate this agent's secret first: it was issued by someone else, and team/department grants are the owner's.");
        await db.Grants.Where(g => g.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        db.Grants.AddRange(command.Grants.Select(g => new AgentGrant
        {
            AgentId = command.AgentId,
            Scope = g.Scope,
            CanSearch = g.CanSearch,
            CanUpsert = g.CanUpsert
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Outcome<IReadOnlyList<GrantEntry>>.Ok(command.Grants);
    }
}
