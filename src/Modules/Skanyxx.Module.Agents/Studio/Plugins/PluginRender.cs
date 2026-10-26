using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>Every file of a plugin as it should be in main (P1): what an install proposes and what a catalog proposal is checked against.</summary>
internal static class PluginRender
{
    public static Dictionary<string, string> Files(PluginDraft draft, string ns)
    {
        var files = new Dictionary<string, string> { [StudioNames.PluginManifestPath(draft.Name)] = PluginManifest.Render(InstalledPlugin.From(draft)) };
        foreach (var server in draft.Servers)
            files[StudioNames.ServerPath(draft.Name, server.Name)] = McpServerManifest.Render(server, ns);
        foreach (var agent in draft.Agents)
            (files[StudioNames.AgentPath(agent.Name)], files[StudioNames.GrantsPath(agent.Name)]) = AgentManifest.Render(agent, ns);
        return files;
    }
}
