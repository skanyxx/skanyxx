using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Moves one run by one stage and saves the attempt, its agent turns and the new cursor in ONE transaction.
/// A crash mid-stage therefore leaves nothing half-written: after a restart the stage simply runs again.
/// A step that ends the run tells memory, after the save (<see cref="RunFinishedNotice"/>). Each stage is picked up only
/// while its starter is still an enabled supervisor or the owner (D395): a role saved away between the request and the
/// work, or a revocation that raced a start, still stops the run. A gate's approval resumes through here too.
/// </summary>
public sealed class RunStepper(
    TicketsDbContext db, StageRunner runner, IOptions<TicketsOptions> options, TimeProvider clock, IPublisher publisher, IPersonStanding standing)
{
    private const string StarterRefused = "cancelled: the person who started it may no longer run it (disabled, or no longer a supervisor)";

    private static readonly string[] Starters = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor];

    /// <returns>Whether the run moved (and may move again).</returns>
    public async Task<bool> StepAsync(Guid runId, CancellationToken ct)
    {
        // Earlier attempts' prompts and answers are not needed to run the next stage, only their outputs.
        var run = await db.Runs.Include(r => r.StageRuns).SingleAsync(r => r.Id == runId, ct);
        if (run.State is RunState.Pending or RunState.Running && !(await standing.ActiveRolesAsync(run.CreatedBy, ct)).Overlaps(Starters))
        {
            run.State = RunState.Cancelled;
            run.Error = StarterRefused;
            await SaveAsync(run, ct); // a cancelled run is not finished work: memory is not told
            return false;
        }

        if (run.State == RunState.Pending)
        {
            // Saved before the agent is called, so the run shows as running (with its stage) while it works.
            run.State = RunState.Running;
            if (!await SaveAsync(run, ct))
                return false;
        }

        if (run.State != RunState.Running)
            return false;

        if (run.StageRuns.Count >= options.Value.MaxStageAttemptsPerRun)
        {
            run.State = RunState.Failed;
            run.Error = $"stage attempt budget spent: this run already made {run.StageRuns.Count} stage attempts (Tickets:MaxStageAttemptsPerRun)";
        }
        else
        {
            var stage = run.CurrentStage;
            var attempt = await runner.RunAsync(run, ct);
            run.StageRuns.Add(attempt);
            // The rejection has been shown to the stage it was for; a later pass must not quote it again.
            run.LoopedFrom.Remove(stage.Id);
            switch (attempt.State)
            {
                case StageState.AwaitingHuman:
                    run.State = RunState.AwaitingHuman;
                    break;
                case StageState.Failed:
                    RunTransitions.Fail(run, attempt);
                    break;
                default:
                    RunTransitions.Pass(run);
                    break;
            }
        }

        if (!await SaveAsync(run, ct))
            return false;
        if (RunFinishedNotice.From(run) is { } finished)
            await publisher.Publish(finished, ct);
        return run.State == RunState.Running;
    }

    /// <summary>
    /// Ends a run that could not be stepped, saying why. A direct update rather than load-and-save: the row may be
    /// exactly what cannot be loaded. A run this ended is finished work too, so memory is told (C-6) — from the full run
    /// when it loads, otherwise from its plain columns with the reason as the outcome.
    /// </summary>
    public async Task FailAsync(Guid runId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ended = await db.Runs
            .Where(r => r.Id == runId && (r.State == RunState.Pending || r.State == RunState.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.State, RunState.Failed)
                .SetProperty(r => r.Error, reason)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.Version, r => r.Version + 1), ct);
        if (ended == 1)
            await publisher.Publish(await FailedNoticeAsync(runId, reason, ct), ct);
    }

    private async Task<TicketRunFinished> FailedNoticeAsync(Guid runId, string reason, CancellationToken ct)
    {
        try
        {
            var run = await db.Runs.AsNoTracking().Include(r => r.StageRuns).SingleAsync(r => r.Id == runId, ct);
            return RunFinishedNotice.From(run)!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var plain = await db.Runs.AsNoTracking().Where(r => r.Id == runId)
                .Select(r => new { r.TicketKey, r.CreatedBy, r.PipelineName }).SingleAsync(ct);
            return new TicketRunFinished(runId, plain.CreatedBy, plain.TicketKey, plain.TicketKey, null, plain.PipelineName,
                Succeeded: false, TextCut.Take(reason, RunFinishedNotice.OutcomeChars), [], []);
        }
    }

    private async Task<bool> SaveAsync(Run run, CancellationToken ct)
    {
        run.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        run.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Cancelled while the agent was working: the person's change wins and this attempt is dropped.
            return false;
        }
    }
}
