namespace Skanyxx.Core.Platform.Studio;

/// <param name="Agent">The agent the proposal's files are for; null when the files are not one agent's.</param>
/// <param name="ProposedBy">The Skanyxx user who proposed it (from the PR's trailer, which only Skanyxx writes).</param>
/// <param name="Plugin">The plugin a plugin proposal installs, changes or removes (its trailer); null for an agent proposal.</param>
public sealed record ProposalSummary(int Number, string Title, string? Agent, string? ProposedBy, string HeadSha, bool Mergeable, string? Plugin = null);
