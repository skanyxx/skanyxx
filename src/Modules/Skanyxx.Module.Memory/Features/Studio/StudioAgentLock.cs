using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features.Grants;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// The studio's writes take the per-agent grants lock (<see cref="AgentGrantLock"/>), so they serialize with the REST
/// grant and secret calls for the same agent, and touch only names the studio owns (D117): one it claimed before, or one
/// memory knows nothing about (claimed now, when <c>claim</c>). An agent whose secret acts for users (D084), or any other
/// principal someone made by hand, is refused. Must run inside a transaction.
/// </summary>
internal static class StudioAgentLock
{
    public const string ActsForUsers = "This agent acts for users: it is the owner's, not the studio's.";

    public const string Suspended = "The owner suspended this studio agent: nothing is issued or granted until it is resumed.";

    public static string NotStudios(string agentId) =>
        $"Memory already has an agent '{agentId}' that the studio did not create (the owner's, or an external client's); the studio never takes it over. Choose another name.";

    /// <returns>The studio's row (null when the name is free and not claimed), or the reason the name is not the studio's.</returns>
    public static async Task<(StudioAgent? Agent, string? Refused)> AcquireAsync(MemoryDbContext db, string agentId, bool claim, CancellationToken ct)
    {
        await AgentGrantLock.AcquireAsync(db, agentId, ct);
        var secret = await db.AgentSecrets.AsNoTracking().FirstOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (secret is { ActsForUsers: true })
            return (null, ActsForUsers);
        if (await db.StudioAgents.FirstOrDefaultAsync(a => a.AgentId == agentId, ct) is { } owned)
            return (owned, null);
        if (secret is not null || await db.Grants.AnyAsync(g => g.AgentId == agentId, ct))
            return (null, NotStudios(agentId));
        if (!claim)
            return (null, null);
        var claimed = new StudioAgent { AgentId = agentId, ClaimedAt = DateTime.UtcNow };
        db.StudioAgents.Add(claimed);
        await db.SaveChangesAsync(ct);
        return (claimed, null);
    }

    /// <summary>A short, stable name for a secret (D118): the start of its stored SHA-256, so it reveals nothing.</summary>
    public static string Fingerprint(byte[] secretHash) => Convert.ToHexStringLower(secretHash)[..12];
}
