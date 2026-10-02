using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Grants;

internal sealed class GetAgentGrantsHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<GetAgentGrantsQuery, Outcome<IReadOnlyList<GrantEntry>>>
{
    public async Task<Outcome<IReadOnlyList<GrantEntry>>> Handle(GetAgentGrantsQuery query, CancellationToken ct)
    {
        if (!access.IsSupervisor(query.Caller))
            return Outcome<IReadOnlyList<GrantEntry>>.Forbidden("Only a supervisor may read agent grants.");

        var grants = await db.Grants.AsNoTracking()
            .Where(g => g.AgentId == query.AgentId)
            .OrderBy(g => g.Scope)
            .Select(g => new GrantEntry(g.Scope, g.CanSearch, g.CanUpsert))
            .ToListAsync(ct);
        return Outcome<IReadOnlyList<GrantEntry>>.Ok(grants);
    }
}
