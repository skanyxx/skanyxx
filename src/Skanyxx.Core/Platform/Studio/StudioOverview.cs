namespace Skanyxx.Core.Platform.Studio;

/// <param name="Repo">The agent-config repo, <c>org/name</c>; null when no git is configured.</param>
/// <param name="RepoProblem">Why the repo could not be read or created (git down, not configured); null when fine.</param>
public sealed record StudioOverview(
    string? Repo, string? RepoProblem, IReadOnlyList<ProposalSummary> Open, IReadOnlyList<StudioAgentSummary> Agents);
