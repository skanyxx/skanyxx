using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <param name="Agent">An agent proposal's one agent folder; null for a plugin proposal or files that are not one agent's.</param>
/// <param name="Draft">The agent the files describe; null when they are not valid (see <paramref name="Problems"/>) or for a plugin proposal.</param>
/// <param name="NeedsOwner">The head or main grants of this agent open a team or department (D091), or the plugin comes from git or a zip (D222).</param>
internal sealed record InspectedProposal(
    RepoPull Pull, string? Agent, AgentDraft? Draft, IReadOnlyList<ProposalFile> Files, IReadOnlyList<string> Problems, bool NeedsOwner)
{
    /// <summary>The plugin of a plugin proposal (P8); null for an agent proposal.</summary>
    public string? Plugin { get; init; }

    /// <summary>A plugin proposal's agents and servers at its head, when valid.</summary>
    public IReadOnlyList<AgentDraft> Drafts { get; init; } = [];

    public IReadOnlyList<PluginServerDraft> Servers { get; init; } = [];

    /// <summary>A plugin proposal that removes the plugin (its <c>plugin.yaml</c> is deleted).</summary>
    public bool Removal { get; init; }

    /// <summary>What the reviewer should know that does not stop the merge.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool Valid => Problems.Count == 0;
}
