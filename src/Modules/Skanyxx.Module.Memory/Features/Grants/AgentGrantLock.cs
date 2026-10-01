using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

/// <summary>
/// Serializes everything that reads and writes one agent's grants, and answers whether the agent holds a team or
/// department grant — the owner's to set, and so the owner's to change or to hand a new secret for (D091).
/// Must run inside a transaction.
/// </summary>
internal static class AgentGrantLock
{
    /// <summary>First key of the two-key advisory lock, so this feature's locks never collide with other lock users.</summary>
    private const int GrantsLockSpace = 0x4D454D01;

    public static Task AcquireAsync(MemoryDbContext db, string agentId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({GrantsLockSpace}, hashtext({agentId}))", ct);

    public static async Task<bool> HoldsTeamOrDepartmentAsync(MemoryDbContext db, string agentId, CancellationToken ct) =>
        (await db.Grants.AsNoTracking().Where(g => g.AgentId == agentId).ToListAsync(ct))
        .Any(g => OpensTeamOrDepartment(g.Scope, g.CanSearch, g.CanUpsert));

    /// <summary>
    /// The one definition of "a team or department grant". A row with both flags off is how an agent is revoked, and
    /// opens nothing, so it does not count: an owner's revoke must not lock supervisors out of the agent for good.
    /// </summary>
    public static bool OpensTeamOrDepartment(string grantScope, bool canSearch, bool canUpsert) =>
        (canSearch || canUpsert) && Scope.TryParse(grantScope, out var scope) && scope.Level is ScopeLevel.Team or ScopeLevel.Department;
}
