using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// One open proposal per plugin, per agent and per plugin server: a supervisor never merges two competing versions of
/// either, and two plugins never race for one name (D222).
/// </summary>
internal static class PluginPending
{
    /// <param name="open">The open proposals to compare with (a proposal being inspected is left out of them).</param>
    /// <returns>Why a proposal for <paramref name="plugin"/> must wait, or null.</returns>
    public static async Task<string?> FindAsync(IAgentRepo repo, IEnumerable<RepoPull> open, string plugin, IReadOnlyCollection<string> agents,
        IReadOnlyCollection<string> servers, CancellationToken ct)
    {
        var pulls = open.ToList();
        if (pulls.FirstOrDefault(p => ProposalTrailer.PluginOf(p.Body) == plugin) is { } same)
            return $"The plugin '{plugin}' already has an open proposal (#{same.Number}); a supervisor merges or closes it first.";
        if (pulls.FirstOrDefault(p => ProposalTrailer.AgentOf(p.Body) is { } agent && agents.Contains(agent)) is { } agentPull)
            return $"The agent '{ProposalTrailer.AgentOf(agentPull.Body)}' has an open proposal (#{agentPull.Number}); a supervisor merges or closes it first.";
        foreach (var other in pulls.Where(p => ProposalTrailer.PluginOf(p.Body) is { } name && name != plugin))
        {
            var paths = (await repo.PullFilesAsync(other.Number, ct)).Select(f => f.Path).ToList();
            var why = paths.Select(p => p.Split('/')).FirstOrDefault(p => p is [StudioNames.AgentsFolder, var a, ..] && agents.Contains(a)) is { } path
                ? $"The agent '{path[1]}'"
                : PluginRepoReader.ServerNames(paths).FirstOrDefault(servers.Contains) is { } server ? $"The server '{server}'" : null;
            if (why is not null)
                return $"{why} is in the open proposal of the plugin '{ProposalTrailer.PluginOf(other.Body)}' (#{other.Number}); a supervisor merges or closes it first.";
        }
        return null;
    }
}
