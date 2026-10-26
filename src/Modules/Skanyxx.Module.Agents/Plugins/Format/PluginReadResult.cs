using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// What a source holds: a marketplace listing, one plugin, or neither, with <see cref="Problem"/> saying why the whole
/// thing was refused. Exactly one of the three is set.
/// </summary>
internal sealed record PluginReadResult(PluginMarketplace? Marketplace, PluginDraft? Plugin, string? Problem)
{
    public static PluginReadResult Listing(PluginMarketplace marketplace) => new(marketplace, null, null);

    public static PluginReadResult Translated(PluginDraft plugin) => new(null, plugin, null);

    public static PluginReadResult Refused(string problem) => new(null, null, problem);
}
