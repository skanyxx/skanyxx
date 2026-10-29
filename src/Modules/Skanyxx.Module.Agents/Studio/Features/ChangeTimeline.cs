using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// A change's timeline (D365): draft → pull request opened → checks → merged → deployed, each done, active, failed,
/// waiting or skipped, with one plain sentence. Pure, so every combination is tested without git.
/// </summary>
internal static class ChangeTimeline
{
    /// <param name="deployedNote">For a merged change with no deploy status of its own (a later change replaced it, a plugin): why.</param>
    public static IReadOnlyList<ChangeStep> Build(ChangeStatus status, ChangeChecks checks, DeployStatus? deploy, bool requireChecks, string? deployedNote = null)
    {
        var closed = status == ChangeStatus.Closed;
        return
        [
            new("Draft", StepState.Done, "Written in the studio form."),
            new("Pull request opened", StepState.Done, "One commit on its own branch, waiting for review."),
            Checks(status, checks, requireChecks),
            status switch
            {
                ChangeStatus.Merged => new("Merged", StepState.Done, "A supervisor (or the owner) merged it into main."),
                ChangeStatus.Closed => new("Merged", StepState.Skipped, "Closed without merging; nothing changed."),
                _ => new("Merged", checks.State == CheckState.Failed && requireChecks ? StepState.Waiting : StepState.Active,
                    "Waiting for a supervisor (or the owner) to merge it.")
            },
            closed ? new("Deployed", StepState.Skipped, "Nothing to deploy.")
                : status == ChangeStatus.Merged && deploy is null && deployedNote is not null ? new("Deployed", StepState.Done, deployedNote)
                : Deployed(status, deploy)
        ];
    }

    private static ChangeStep Checks(ChangeStatus status, ChangeChecks checks, bool requireChecks) => checks.State switch
    {
        CheckState.Passed => new("Checks", StepState.Done, $"{checks.Runs.Count} check(s) passed."),
        CheckState.Failed => new("Checks", StepState.Failed,
            $"Failed: {string.Join(", ", checks.Runs.Where(r => r.State == CheckState.Failed).Select(r => r.Name))}."),
        CheckState.Pending when status == ChangeStatus.Open => new("Checks", StepState.Active, "Running."),
        CheckState.Pending => new("Checks", StepState.Skipped, "Still pending when it was merged or closed."),
        _ => new("Checks", StepState.Skipped, requireChecks
            ? "No CI has reported yet; a merge waits for it."
            : "No CI reported (optional: see the setup guide).")
    };

    private static ChangeStep Deployed(ChangeStatus status, DeployStatus? deploy)
    {
        if (status != ChangeStatus.Merged || deploy is null)
            return new("Deployed", StepState.Waiting, "After the merge, it is applied to kagent.");
        return deploy.State switch
        {
            DeployState.Healthy => new("Deployed", StepState.Done, deploy.Detail),
            DeployState.Failed => new("Deployed", StepState.Failed, deploy.Detail),
            DeployState.Suspended => new("Deployed", StepState.Skipped, deploy.Detail),
            _ => new("Deployed", StepState.Active, deploy.Detail)
        };
    }
}
