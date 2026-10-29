using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>
/// D392: only supervisors and the owner start runs, so a person who loses supervisor, is disabled or is refused by the
/// Entra re-check must not keep runs calling agents on their earlier authority. Every unfinished run they started is
/// cancelled, its waiting gate skipped, in one local transaction; a stage in flight then loses its save on the version
/// bump and is dropped, as with a person's cancel. Idempotent: a second publish finds nothing unfinished.
/// </summary>
[NotificationOrder(NotificationOrderAttribute.Revocation)]
internal sealed class CancelRunsOnPrivilegesRevoked(TicketsDbContext db, TimeProvider clock, ILogger<CancelRunsOnPrivilegesRevoked> logger)
    : INotificationHandler<PrivilegesRevoked>
{
    public async Task Handle(PrivilegesRevoked notification, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var error = $"cancelled: the person who started it lost the right to run it ({notification.Reason})";
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var runs = db.Runs.Where(r => r.CreatedBy == notification.UserId
            && (r.State == RunState.Pending || r.State == RunState.Running || r.State == RunState.AwaitingHuman));
        await db.StageRuns.Where(s => s.State == StageState.AwaitingHuman && runs.Any(r => r.Id == s.RunId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StageState.Skipped).SetProperty(x => x.EndedAt, now), ct);
        var cancelled = await runs.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.State, RunState.Cancelled)
            .SetProperty(r => r.Error, error)
            .SetProperty(r => r.UpdatedAt, now)
            .SetProperty(r => r.Version, r => r.Version + 1), ct);
        await transaction.CommitAsync(ct);
        if (cancelled > 0)
            logger.LogWarning("{Count} unfinished ticket runs of {UserId} cancelled ({Reason})", cancelled, notification.UserId, notification.Reason);
    }
}
