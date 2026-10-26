namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>A marketplace's plugins, each with the request that inspects or installs it.</summary>
internal sealed record PluginListing(string Name, string Description, IReadOnlyList<PluginListingEntry> Entries);
