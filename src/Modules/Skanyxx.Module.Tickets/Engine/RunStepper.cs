using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Moves one run by one stage and saves the attempt, its agent turns and the new cursor in ONE transaction.
/// A crash mid-stage therefore leaves nothing half-written: after a restart the stage simply runs again.
/// </summary>
public sealed class RunStepper(TicketsDbContext db, StageRunner runner, IOptions<TicketsOptions> options, TimeProvider clock)
{
    /// <returns>Whether the run moved (and may move again).</returns>
    public async Task<bool> StepAsync(Guid runId, CancellationToken ct)
    {
        // Earlier attempts' prompts and answers are not needed to run the next stage, only their outputs.
        var run = await db.Runs.Include(r => r.StageRuns).SingleAsync(r => r.Id == runId, ct);
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

        return await SaveAsync(run, ct) && run.State == RunState.Running;
    }

    /// <summary>
    /// Ends a run that could not be stepped, saying why. A direct update rather than load-and-save: the row may be
    /// exactly what cannot be loaded.
    /// </summary>
    public Task FailAsync(Guid runId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.Runs
            .Where(r => r.Id == runId && (r.State == RunState.Pending || r.State == RunState.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.State, RunState.Failed)
                .SetProperty(r => r.Error, reason)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.Version, r => r.Version + 1), ct);
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
