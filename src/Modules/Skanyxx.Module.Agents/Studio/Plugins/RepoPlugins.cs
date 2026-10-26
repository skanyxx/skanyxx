using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// The plugins of one branch or commit. Invalid manifests and server files are kept with their reason, never applied and
/// never removed (like an invalid agent folder).
/// </summary>
/// <param name="ServerPlugins">
/// Every <c>plugins/&lt;p&gt;/mcp/&lt;s&gt;.yaml</c>, valid or not, as server → the plugin folder holding it: the plugin part
/// of the MCP allow-list (P7), and whose third-party server an agent uses (D219).
/// </param>
/// <param name="SharedAgents">Agents more than one plugin manifest lists: they belong to none and are never applied.</param>
internal sealed record RepoPlugins(
    IReadOnlyDictionary<string, InstalledPlugin> Plugins,
    IReadOnlyDictionary<string, string> Problems,
    IReadOnlyDictionary<string, PluginServerEntry> Servers,
    IReadOnlyDictionary<string, string> ServerProblems,
    IReadOnlyDictionary<string, string> ServerPlugins,
    IReadOnlySet<string> SharedAgents)
{
    public IReadOnlySet<string> ServerNames { get; } = ServerPlugins.Keys.ToHashSet();

    /// <summary>The plugin whose valid manifest lists <paramref name="agent"/>, or null.</summary>
    public string? OwnerOf(string agent) => Plugins.Values.FirstOrDefault(p => p.Agents.Contains(agent))?.Name;

    /// <summary>The plugin whose server file or valid manifest has <paramref name="server"/>, or null.</summary>
    public string? ServerOwnerOf(string server) =>
        Servers.TryGetValue(server, out var entry) ? entry.Plugin : Plugins.Values.FirstOrDefault(p => p.Servers.Contains(server))?.Name;
}
