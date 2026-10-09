using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>Read only, no lock: the propose and merge checks (D117) and the reconciler's view of one name.</summary>
internal sealed class StudioPrincipalHandler(MemoryDbContext db) : IRequestHandler<StudioPrincipalQuery, Outcome<StudioPrincipal>>
{
    public async Task<Outcome<StudioPrincipal>> Handle(StudioPrincipalQuery query, CancellationToken ct)
    {
        var secret = await db.AgentSecrets.AsNoTracking().FirstOrDefaultAsync(s => s.AgentId == query.AgentId, ct);
        var studio = await db.StudioAgents.AsNoTracking().FirstOrDefaultAsync(a => a.AgentId == query.AgentId, ct);
        var grants = await db.Grants.AsNoTracking().Where(g => g.AgentId == query.AgentId).OrderBy(g => g.Scope)
            .Select(g => new StudioGrant(g.Scope, g.CanSearch, g.CanUpsert)).ToListAsync(ct);
        var taken = secret is { ActsForUsers: true } || (studio is null && (secret is not null || grants.Count > 0));
        var fingerprint = studio is not null && secret is { CreatedBy: StudioSecret.IssuedBy, ActsForUsers: false }
            ? StudioAgentLock.Fingerprint(secret.SecretHash)
            : null;
        return Outcome<StudioPrincipal>.Ok(new StudioPrincipal(query.AgentId, taken, studio is not null, studio?.Suspended == true, fingerprint,
            studio?.DeployedFingerprint, grants));
    }
}
