using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Audit;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>
/// A queued run is cancelled at once. A running one is asked to stop through the database, so whichever replica runs
/// it sees the request within <see cref="InvestigateOptions.WatchMilliseconds"/>; the answer is 202 until it has.
/// A finished run is a 409.
/// </summary>
internal sealed class CancelRunHandler(InvestigateDbContext db, OrgTeams org, InvestigateAudit audit, TimeProvider clock) : IRequestHandler<CancelRunCommand, Outcome<RunDto>>
{
    public async Task<Outcome<RunDto>> Handle(CancelRunCommand command, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == command.Id, ct);
        if (run is null || !WorkflowAccess.CanSee(run, command.Viewer, await org.ReachAsync(command.Viewer, ct)))
            return Outcome<RunDto>.NotFound("No such investigation run.");

        var now = clock.GetUtcNow().UtcDateTime;
        var dequeued = await db.Runs.Where(r => r.Id == run.Id && r.State == RunState.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RunState.Cancelled).SetProperty(r => r.CancelRequested, true)
                .SetProperty(r => r.Failure, "Cancelled.").SetProperty(r => r.EndedAt, now), ct) == 1;
        if (dequeued)
            await db.StepRuns.Where(s => s.RunId == run.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Skipped), ct);
        var asked = dequeued || await db.Runs.Where(r => r.Id == run.Id && r.State == RunState.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true), ct) == 1;

        var current = await db.Runs.AsNoTracking().Include(r => r.StepRuns).SingleAsync(r => r.Id == run.Id, ct);
        if (!asked)
            return Outcome<RunDto>.Conflict(RunMapper.Full(current, command.Viewer), "This investigation has already finished.", "finished");

        await audit.RunAsync(InvestigateAudit.RunCancelRequested, command.Viewer.UserId, run.Id, run.WorkflowId, run.WorkflowName, current.State.ToString().ToLowerInvariant());
        return dequeued ? Outcome<RunDto>.Ok(RunMapper.Full(current, command.Viewer)) : Outcome<RunDto>.Accepted(RunMapper.Full(current, command.Viewer));
    }
}
