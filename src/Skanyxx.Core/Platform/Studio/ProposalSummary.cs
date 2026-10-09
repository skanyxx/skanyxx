namespace Skanyxx.Core.Platform.Studio;

/// <param name="Agent">The agent the proposal's files are for; null when the files are not one agent's.</param>
/// <param name="ProposedBy">The Skanyxx user who proposed it (from the PR's trailer, which only Skanyxx writes).</param>
public sealed record ProposalSummary(int Number, string Title, string? Agent, string? ProposedBy, string HeadSha, bool Mergeable);
