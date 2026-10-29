using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Deploy;

/// <summary>
/// One merged agent's deploy status without Argo (D363): what the reconciler applied to kagent and what kagent says of it.
/// </summary>
internal static class KAgentStatusMap
{
    public const string Source = "reconciler";

    /// <param name="live">kagent's agent of that name in the studio's namespace; null when kagent has none.</param>
    /// <param name="last">The last reconcile pass of this replica; null before the first.</param>
    public static DeployStatus For(Agent? live, ReconcilePass? last)
    {
        if (live is null || live.Labels.GetValueOrDefault(StudioNames.MergedLabel) != "true")
            return last is { Failed: true } or { Failures: > 0 }
                ? new DeployStatus(DeployState.Failed, Source, "Not in kagent: the last reconcile pass could not apply everything; it retries on its next pass.")
                : new DeployStatus(DeployState.Waiting, Source, "Not in kagent yet: the reconciler applies it on its next pass.");
        if (live.Suspended)
            return new DeployStatus(DeployState.Suspended, Source, "Scaled to zero in kagent.");
        if (live.Ready)
            return new DeployStatus(DeployState.Healthy, Source, "Live in kagent and in Chat.");
        // A fresh agent has no conditions for a moment: not ready is "starting", whatever Accepted says yet.
        return new DeployStatus(DeployState.Syncing, Source, live.Accepted ? "Applied to kagent; its pod is starting." : "Applied; kagent has not accepted it yet.");
    }
}
