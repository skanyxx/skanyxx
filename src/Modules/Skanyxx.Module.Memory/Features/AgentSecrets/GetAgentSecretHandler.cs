using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

internal sealed class GetAgentSecretHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<GetAgentSecretQuery, Outcome<AgentSecretStatus>>
{
    public async Task<Outcome<AgentSecretStatus>> Handle(GetAgentSecretQuery query, CancellationToken ct)
    {
        if (!access.IsSupervisor(query.Caller))
            return Outcome<AgentSecretStatus>.Forbidden("Only a supervisor may read agent secrets.");

        var status = await db.AgentSecrets.AsNoTracking()
            .Where(s => s.AgentId == query.AgentId)
            .Select(s => new AgentSecretStatus(s.AgentId, true, s.CreatedAt, s.ActsForUsers, s.CreatedBy))
            .SingleOrDefaultAsync(ct);
        return Outcome<AgentSecretStatus>.Ok(status ?? new AgentSecretStatus(query.AgentId, false, null, false, null));
    }
}
