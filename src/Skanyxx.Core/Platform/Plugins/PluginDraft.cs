using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// A Claude Code plugin translated into what the studio can propose: its agents as studio drafts (no grants, no skills,
/// no TTL memory: recipes only, D005), its remote MCP servers, and a note for everything that was renamed, dropped or
/// refused. An agent tool choice with no tool names means every tool of that plugin server.
/// </summary>
public sealed record PluginDraft(
    string Name,
    string? Version,
    string Description,
    PluginSource Source,
    IReadOnlyList<AgentDraft> Agents,
    IReadOnlyList<PluginServerDraft> Servers,
    IReadOnlyList<PluginNote> Notes);
