using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <param name="Draft">The agent the files describe; null when they are not valid (see <paramref name="Problems"/>).</param>
/// <param name="NeedsOwner">The head or main grants of this agent open a team or department (D091).</param>
internal sealed record InspectedProposal(
    RepoPull Pull, string? Agent, AgentDraft? Draft, IReadOnlyList<ProposalFile> Files, IReadOnlyList<string> Problems, bool NeedsOwner);
