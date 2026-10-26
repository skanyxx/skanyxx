using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>
/// What installing the plugin would propose: its agents and remote MCP servers, and a note for everything renamed, left
/// out or refused. Header values are never in it, only whether the plugin carries one. <see cref="Digest"/> goes back
/// with the install request.
/// </summary>
internal sealed record PluginReport(
    string Name,
    string? Version,
    string Description,
    string Digest,
    PluginSource Source,
    IReadOnlyList<PluginReportAgent> Agents,
    IReadOnlyList<PluginReportServer> Servers,
    IReadOnlyList<PluginNote> Notes)
{
    public const string ValueIncluded = "value included";

    public static PluginReport From(PluginDraft draft) => new(
        draft.Name, draft.Version, draft.Description, draft.Source.Digest, draft.Source,
        [.. draft.Agents.Select(a => new PluginReportAgent(a.Name, a.Description, a.ModelConfig, a.McpTools))],
        [.. draft.Servers.Select(s => new PluginReportServer(s.Name, s.Description, s.Url, s.Protocol,
            [.. s.Headers.Select(h => new PluginReportHeader(h.Name, h.Template, h.Value is null ? null : ValueIncluded))]))],
        draft.Notes);
}
