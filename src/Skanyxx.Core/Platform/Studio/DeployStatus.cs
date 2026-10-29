namespace Skanyxx.Core.Platform.Studio;

/// <param name="Source"><c>argo</c> when Studio:Argo is configured, else <c>reconciler</c>.</param>
/// <param name="Detail">One sentence for people; fixed texts and the deploy tool's own status words, never its error bodies.</param>
public sealed record DeployStatus(DeployState State, string Source, string Detail);
