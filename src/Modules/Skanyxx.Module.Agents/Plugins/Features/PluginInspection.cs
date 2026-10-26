namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>What a source holds: a marketplace (<see cref="Marketplace"/>) or one plugin (<see cref="Plugin"/>); exactly one is set.</summary>
internal sealed record PluginInspection(PluginListing? Marketplace, PluginReport? Plugin);
