using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>A plugin's <c>plugin.yaml</c>: where it came from, the agents and servers it owns, its headers' templates and its install report.</summary>
internal sealed record InstalledPlugin(
    string Name,
    string? Version,
    string Description,
    PluginSource Source,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Servers,
    IReadOnlyList<PluginHeaderTemplate> Headers,
    IReadOnlyList<PluginNote> Notes)
{
    public static InstalledPlugin From(PluginDraft draft) => new(
        draft.Name, draft.Version, draft.Description, draft.Source,
        [.. draft.Agents.Select(a => a.Name)],
        [.. draft.Servers.Select(s => s.Name)],
        [.. draft.Servers.SelectMany(s => s.Headers.Select(h => new PluginHeaderTemplate(s.Name, h.Name, h.Template)))],
        draft.Notes);
}
