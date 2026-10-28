using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Sessions;

/// <summary>Chat's agent details: the agent's grants as stored (none = the default, D040). Scopes only, never a card.</summary>
internal sealed class AgentMemoryAccess(MemoryDbContext db) : IAgentMemoryAccess
{
    public async Task<IReadOnlyList<AgentMemoryGrant>?> ForAgentAsync(string agentId, CancellationToken ct) =>
        await db.Grants.AsNoTracking()
            .Where(g => g.AgentId == agentId)
            .OrderBy(g => g.Scope)
            .Select(g => new AgentMemoryGrant(g.Scope, g.CanSearch, g.CanUpsert))
            .ToListAsync(ct);
}
