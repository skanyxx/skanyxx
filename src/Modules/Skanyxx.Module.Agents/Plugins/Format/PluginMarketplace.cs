namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>A <c>.claude-plugin/marketplace.json</c>: its name and one entry per plugin it lists.</summary>
internal sealed record PluginMarketplace(string Name, string Description, IReadOnlyList<MarketplaceEntry> Entries);
