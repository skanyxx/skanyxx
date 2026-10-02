using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary>
/// Offboarding: a person who loses supervisor or is disabled must not keep agent access through secrets they minted
/// and kept a copy of. Deletes every secret whose <c>created_by</c> is them, in one statement; the agents are locked
/// out of <c>/mcp/memory</c> until a supervisor issues new ones. An owner's secrets (acts-for-users included) are the
/// owner's, whom neither event reaches.
/// </summary>
internal sealed class RevokeSecretsOnPrivilegesRevoked(MemoryDbContext db, ILogger<RevokeSecretsOnPrivilegesRevoked> logger)
    : INotificationHandler<PrivilegesRevoked>
{
    public async Task Handle(PrivilegesRevoked notification, CancellationToken ct)
    {
        var agents = await db.Database
            .SqlQuery<string>($"DELETE FROM memory_agent_secrets WHERE created_by = {notification.UserId} RETURNING agent_id AS \"Value\"")
            .ToListAsync(ct);
        foreach (var agent in agents)
            logger.LogWarning("Agent memory secret for {AgentId} revoked: its issuer {IssuerUserId} lost privileges ({Reason})",
                agent, notification.UserId, notification.Reason);
    }
}
