using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>The grants of a studio agent from git (D029), only for a name the studio owns (D117) and not suspended (D121).</summary>
internal sealed class SetStudioGrantsHandler(MemoryDbContext db, ILogger<SetStudioGrantsHandler> logger)
    : IRequestHandler<SetStudioGrantsCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(SetStudioGrantsCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (agent, refused) = await StudioAgentLock.AcquireAsync(db, command.AgentId, claim: true, ct);
        if (refused is not null)
            return Outcome<bool>.Forbidden(refused);
        if (agent!.Suspended)
            return Outcome<bool>.Forbidden(StudioAgentLock.Suspended);

        var current = await db.Grants.AsNoTracking().Where(g => g.AgentId == command.AgentId)
            .Select(g => new { g.Scope, g.CanSearch, g.CanUpsert }).ToListAsync(ct);
        var wanted = command.Grants.Select(g => new { g.Scope, CanSearch = g.Search, CanUpsert = g.Upsert }).ToList();
        if (current.Count == wanted.Count && current.All(wanted.Contains))
        {
            await tx.CommitAsync(ct); // keeps a claim made just now
            return Outcome<bool>.Ok(false);
        }

        await db.Grants.Where(g => g.AgentId == command.AgentId).ExecuteDeleteAsync(ct);
        db.Grants.AddRange(command.Grants.Select(g => new AgentGrant
        {
            AgentId = command.AgentId, Scope = g.Scope, CanSearch = g.Search, CanUpsert = g.Upsert
        }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // Same audit line shape as the REST grant change (D091): this is the agent's access history.
        logger.LogWarning("Agent memory grants set for {AgentId} by the studio for {Actor}: {Grants}", command.AgentId, command.Actor,
            command.Grants.Count == 0 ? "none" : string.Join(", ", command.Grants.Select(g => $"{g.Scope} (search: {g.Search}, upsert: {g.Upsert})")));
        return Outcome<bool>.Ok(true);
    }
}
