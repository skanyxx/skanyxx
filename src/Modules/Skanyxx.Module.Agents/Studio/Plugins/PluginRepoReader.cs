using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Agents.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// Reads the plugin files of the agent repo (P1): <c>plugins/&lt;p&gt;/plugin.yaml</c> and <c>plugins/&lt;p&gt;/mcp/*.yaml</c>,
/// each strict (<see cref="PluginManifest"/>, <see cref="McpServerManifest"/>) and judged (<see cref="PluginRules"/>). A
/// server is valid only when its plugin's manifest is valid, lists it with exactly its headers, and no other plugin has a
/// server by that name; a manifest that lists a server whose file is in another plugin's folder is invalid.
/// </summary>
internal sealed class PluginRepoReader(IAgentRepo repo, IOptions<StudioOptions> options, IOptions<PluginsOptions> pluginOptions, ILogger<PluginRepoReader> logger)
{
    private StudioOptions Options => options.Value;

    /// <summary>The plugin folders under <c>plugins/</c>.</summary>
    public static HashSet<string> PluginFolders(IEnumerable<string> paths) =>
        [.. paths.Select(p => p.Split('/')).Where(p => p.Length >= 3 && p[0] == StudioNames.PluginsFolder).Select(p => p[1])];

    /// <summary>Every server file's name: the plugin part of the MCP allow-list (P7), without reading a file.</summary>
    public static HashSet<string> ServerNames(IEnumerable<string> paths) =>
        [.. paths.Select(StudioNames.ServerOf).OfType<(string Plugin, string Server)>().Select(s => s.Server)];

    /// <summary>Every server file's name → the plugin folder holding it (the first by name when two plugins have one, which is invalid anyway).</summary>
    public static Dictionary<string, string> ServerPlugins(IEnumerable<string> paths) =>
        paths.Select(StudioNames.ServerOf).OfType<(string Plugin, string Server)>().OrderBy(s => s.Plugin, StringComparer.Ordinal)
            .DistinctBy(s => s.Server).ToDictionary(s => s.Server, s => s.Plugin);

    /// <param name="paths">Every path at <paramref name="reference"/> (main's tree).</param>
    /// <param name="log">Log what is invalid at Error (the reconciler's pass; a page view does not).</param>
    public async Task<RepoPlugins> ReadAsync(IReadOnlyList<string> paths, string reference, bool log, CancellationToken ct)
    {
        var plugins = new Dictionary<string, InstalledPlugin>();
        var problems = new Dictionary<string, string>();
        foreach (var name in PluginFolders(paths).Order(StringComparer.Ordinal))
        {
            var (plugin, problem) = await ReadPluginAsync(name, reference, ct);
            if (plugin is null)
                problems[name] = problem!;
            else
                plugins[name] = plugin;
        }
        // An agent two plugins claim belongs to neither.
        var sharedAgents = new HashSet<string>();
        foreach (var shared in plugins.Values.SelectMany(p => p.Agents.Select(a => (Agent: a, Plugin: p.Name))).GroupBy(a => a.Agent).Where(g => g.Count() > 1).ToList())
        {
            sharedAgents.Add(shared.Key);
            foreach (var (_, plugin) in shared)
            {
                problems[plugin] = $"The agent '{shared.Key}' is listed by more than one plugin.";
                plugins.Remove(plugin);
            }
        }

        // A server is its plugin's own only when its file is in that plugin's folder: a manifest listing another folder's
        // server would make it own for its agents, past the one-plugin rule (D225).
        var files = paths.Select(StudioNames.ServerOf).OfType<(string Plugin, string Server)>().ToList();
        foreach (var plugin in plugins.Values.ToList())
            if (plugin.Servers.FirstOrDefault(s => files.Any(f => f.Server == s && f.Plugin != plugin.Name)) is { } borrowed)
            {
                problems[plugin.Name] = $"It lists the server '{borrowed}', whose file is in another plugin's folder.";
                plugins.Remove(plugin.Name);
            }

        var servers = new Dictionary<string, PluginServerEntry>();
        var serverProblems = new Dictionary<string, string>();
        foreach (var (plugin, server) in files)
        {
            string? problem;
            if (files.Count(f => f.Server == server) > 1)
                problem = $"More than one plugin has a server '{server}'.";
            else if (!plugins.TryGetValue(plugin, out var manifest))
                problem = $"Its plugin '{plugin}' has no valid {StudioNames.PluginFile}.";
            else
                problem = await ReadServerAsync(manifest, server, reference, servers, ct);
            if (problem is not null)
                serverProblems[server] = problem;
        }
        foreach (var missing in plugins.Values.SelectMany(p => p.Servers.Select(s => (Plugin: p.Name, Server: s))).Where(s => !files.Contains(s)))
            serverProblems[missing.Server] = $"{StudioNames.ServerPath(missing.Plugin, missing.Server)} is missing.";

        if (log)
            foreach (var (item, problem) in problems.Select(p => (StudioNames.PluginManifestPath(p.Key), p.Value))
                         .Concat(serverProblems.Select(p => ($"plugin server {p.Key}", p.Value))))
                logger.LogError("Studio skips {Item} in main of {Repo}: {Problem}", item, repo.Name, problem);
        return new RepoPlugins(plugins, problems, servers, serverProblems, ServerPlugins(paths), sharedAgents);
    }

    /// <summary>One plugin's manifest at a branch or commit, valid, or the reason it is not.</summary>
    public async Task<(InstalledPlugin? Plugin, string? Problem)> ReadPluginAsync(string name, string reference, CancellationToken ct)
    {
        if (!StudioNames.IsValidPluginName(name))
            return (null, $"'{name}' is not a valid plugin name.");
        var (text, tooLarge) = await ReadTextAsync(StudioNames.PluginManifestPath(name), reference, ct);
        if (tooLarge)
            return (null, $"{StudioNames.PluginManifestPath(name)} is larger than {IAgentRepo.MaxFileBytes / 1024} KB.");
        if (text is null)
            return (null, $"{StudioNames.PluginFolder(name)} has no {StudioNames.PluginFile}.");
        var (plugin, problem) = PluginManifest.Read(name, text);
        if (plugin is null)
            return (null, problem);
        var problems = PluginRules.CheckPlugin(plugin);
        if (problems.Count > 0)
            return (null, string.Join(" ", problems));
        return SecretScan.Find(text) is { } secret ? (null, $"{StudioNames.PluginManifestPath(name)} seems to hold {secret}.") : (plugin, null);
    }

    /// <summary>One server file of <paramref name="plugin"/>, added to <paramref name="valid"/> when it is valid.</summary>
    public async Task<string?> ReadServerAsync(InstalledPlugin plugin, string server, string reference, Dictionary<string, PluginServerEntry> valid, CancellationToken ct)
    {
        var path = StudioNames.ServerPath(plugin.Name, server);
        if (!plugin.Servers.Contains(server))
            return $"{StudioNames.PluginManifestPath(plugin.Name)} does not list the server '{server}'.";
        var (text, tooLarge) = await ReadTextAsync(path, reference, ct);
        if (tooLarge)
            return $"{path} is larger than {IAgentRepo.MaxFileBytes / 1024} KB.";
        if (text is null)
            return $"{path} is missing.";
        var (draft, problem) = McpServerManifest.Read(server, text, Options.Namespace);
        if (draft is null)
            return problem;
        var problems = PluginRules.CheckServer(draft, Options, pluginOptions.Value);
        if (problems.Count > 0)
            return string.Join(" ", problems);
        var declared = plugin.Headers.Where(h => h.Server == server).Select(h => h.Name).Order(StringComparer.Ordinal);
        if (!declared.SequenceEqual(draft.Headers.Select(h => h.Name).Order(StringComparer.Ordinal)))
            return $"{path} and {StudioNames.PluginManifestPath(plugin.Name)} must name the same headers.";
        if (SecretScan.Find(text) is { } secret)
            return $"{path} seems to hold {secret}.";
        valid[server] = new PluginServerEntry(plugin.Name, draft);
        return null;
    }

    private async Task<(string? Text, bool TooLarge)> ReadTextAsync(string path, string reference, CancellationToken ct)
    {
        try
        {
            return (await repo.ReadAsync(path, reference, ct), false);
        }
        catch (RepoFileTooLargeException)
        {
            return (null, true);
        }
    }
}
