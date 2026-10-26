using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>A plugin in main, as its <c>plugin.yaml</c> describes it.</summary>
/// <param name="Problem">Why its <c>plugin.yaml</c> is not valid (nothing of it is applied); null when valid.</param>
public sealed record PluginSummary(
    string Name,
    string? Version,
    string Description,
    PluginSource? Source,
    IReadOnlyList<PluginServerSummary> Servers,
    IReadOnlyList<StudioAgentSummary> Agents,
    IReadOnlyList<PluginNote> Notes,
    string? Problem)
{
    /// <summary>What does not fit at runtime, e.g. an agent naming a tool its server does not offer.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
