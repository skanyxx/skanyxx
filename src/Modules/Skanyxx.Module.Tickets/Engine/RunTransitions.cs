using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// The one place a stage's outcome moves the run. Both the unattended path and a person's rejection go
/// through <see cref="Fail"/>, so a stage that loops on a failed verdict also loops when a person rejects it.
/// </summary>
public static class RunTransitions
{
    public static void Pass(Run run)
    {
        if (run.Cursor == run.Stages.Count - 1)
            run.State = RunState.Succeeded;
        else
            run.Cursor++;
    }

    public static void Fail(Run run, StageRun failed)
    {
        var stage = run.CurrentStage;
        switch (stage.OnFail)
        {
            case OnFail.Continue:
                Pass(run);
                break;
            case OnFail.Stop:
                run.State = RunState.Failed;
                break;
            case OnFail.Goto:
                Loop(run, stage, failed);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(run), stage.OnFail, null);
        }
    }

    /// <summary>
    /// Send the run back, or refuse and stop — saying why. An exhausted budget must not look like a stage that
    /// failed with <c>on_fail: stop</c>. The target is re-checked here because the pipeline is a snapshot.
    /// </summary>
    private static void Loop(Run run, PipelineStage stage, StageRun failed)
    {
        if (failed.Degraded && failed.DecidedBy is null)
        {
            // Nobody reviewed anything: sending the work back would pay for a re-run that cannot address a finding.
            // (A person rejecting at a gate did review it, and their note goes back with the loop.)
            failed.Warnings.Add("no agent answered this stage, so the run stops instead of looping back");
            run.State = RunState.Failed;
            return;
        }

        var taken = run.Loops.GetValueOrDefault(stage.Id);
        var target = run.Stages.FindIndex(s => s.Id == stage.Goto);
        string? refusal = target switch
        {
            < 0 => $"goto target '{stage.Goto}' is not a stage of this pipeline",
            _ when target >= run.Cursor => $"goto target '{stage.Goto}' is not earlier than '{stage.Id}'",
            _ when taken >= (stage.MaxLoops ?? 0) =>
                $"loop budget spent: '{stage.Id}' already sent this run back {taken} time(s), max_loops is {stage.MaxLoops}",
            _ => null
        };
        if (refusal is not null)
        {
            failed.Warnings.Add(refusal);
            run.State = RunState.Failed;
            return;
        }

        var back = run.Stages[target];
        var pass = run.StageRuns.Count(s => s.StageId == back.Id) + 1;
        failed.Warnings.Add($"looping back to '{back.Name}' (pass {pass})");
        run.Loops[stage.Id] = taken + 1;
        run.LoopedFrom[back.Id] = stage.Id;
        run.Cursor = target;
    }
}
