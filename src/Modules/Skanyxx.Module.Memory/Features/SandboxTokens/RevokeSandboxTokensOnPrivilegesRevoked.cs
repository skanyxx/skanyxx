using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.SandboxTokens;

/// <summary>
/// D076 with D154/D161: a disabled person (or one the Entra re-check refused) loses every sandbox token they own in one
/// local statement, before the sandboxes module tries to stop the sandboxes through AX — a sandbox that would not stop
/// still cannot reach memory. Losing supervisor alone changes nothing: the person's own sandboxes stay theirs.
/// </summary>
[NotificationOrder(NotificationOrderAttribute.Revocation)]
internal sealed class RevokeSandboxTokensOnPrivilegesRevoked(MemoryDbContext db, ILogger<RevokeSandboxTokensOnPrivilegesRevoked> logger)
    : INotificationHandler<PrivilegesRevoked>
{
    public async Task Handle(PrivilegesRevoked notification, CancellationToken ct)
    {
        if (!(notification.AccountDisabled || notification.AccessRemoved))
            return;
        var deleted = await db.SandboxTokens.Where(t => t.OwnerId == notification.UserId).ExecuteDeleteAsync(ct);
        if (deleted > 0)
            logger.LogWarning("{Count} sandbox memory tokens of {UserId} revoked ({Reason})", deleted, notification.UserId, notification.Reason);
    }
}
