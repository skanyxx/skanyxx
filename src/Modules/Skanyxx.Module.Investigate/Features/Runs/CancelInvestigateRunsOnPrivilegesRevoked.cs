using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Endpoints;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>
/// D395: builders and above start investigations, and a run calls agents on its starter's authority, so a person who is
/// disabled, refused by the Entra re-check or left below builder must not keep runs going. Their queued runs are
/// cancelled at once; a running one is asked to stop through the database, as a person's cancel is, and the replica
/// running it stops within <see cref="InvestigateOptions.WatchMilliseconds"/>. A supervisor demoted to builder keeps
/// theirs: builders run investigations. Idempotent: a second publish finds nothing unfinished.
/// </summary>
[NotificationOrder(NotificationOrderAttribute.Revocation)]
internal sealed class CancelInvestigateRunsOnPrivilegesRevoked(
    InvestigateDbContext db, IPersonStanding standing, ILogger<CancelInvestigateRunsOnPrivilegesRevoked> logger) : INotificationHandler<PrivilegesRevoked>
{
    public async Task Handle(PrivilegesRevoked notification, CancellationToken ct)
    {
        if (!notification.AccessRemoved && (await standing.ActiveRolesAsync(notification.UserId, ct)).Overlaps(InvestigateGroup.Roles))
            return;

        var failure = $"Cancelled: the person who started it may no longer run investigations ({notification.Reason}).";
        var runs = db.Runs.Where(r => r.CreatedBy == notification.UserId);
        var queued = runs.Where(r => r.State == RunState.Queued);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.StepRuns.Where(s => queued.Any(r => r.Id == s.RunId)).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Skipped), ct);
        var dequeued = await queued.ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RunState.Cancelled).SetProperty(r => r.CancelRequested, true)
            .SetProperty(r => r.Failure, failure).SetProperty(r => r.EndedAt, r => DateTime.UtcNow), ct); // the database's clock (D373)
        var asked = await runs.Where(r => r.State == RunState.Running).ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true), ct);
        await transaction.CommitAsync(ct);
        if (dequeued + asked > 0)
            logger.LogWarning("{Queued} queued and {Running} running investigations of {UserId} cancelled ({Reason})",
                dequeued, asked, notification.UserId, notification.Reason);
    }
}
