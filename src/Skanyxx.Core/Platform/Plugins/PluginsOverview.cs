using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <param name="Repo">The agent repo, <c>org/name</c>; null when no git is configured.</param>
/// <param name="RepoProblem">Why the repo could not be read (git down, not configured); null when fine.</param>
/// <param name="Open">Open plugin proposals (their <see cref="ProposalSummary.Plugin"/> is set).</param>
public sealed record PluginsOverview(string? Repo, string? RepoProblem, IReadOnlyList<PluginSummary> Plugins, IReadOnlyList<ProposalSummary> Open)
{
    /// <summary>
    /// Outside Development without <c>Identity:DataProtectionCertificatePath</c>: the key ring that protects stored header
    /// values sits unencrypted in the identity database, so the page says so where values are entered.
    /// </summary>
    public bool KeyRingInClear { get; init; }
}
