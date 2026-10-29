using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Plugins;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// CI's question about one agent folder (D362), answered with a merge's rules: the YAML scan and the strict shape
/// (<see cref="AgentManifest.Read"/>), the form's values with the plugin servers in main (a plugin's agent with its own
/// plugin's servers), the cloud tools licence and credentials in its text. Reads git, writes nothing. Advisory: the merge
/// gate checks the same again at the pull request's head.
/// </summary>
internal sealed class ValidateAgentFilesHandler(
    IAgentRepo repo, PluginRepoReader plugins, DraftProblems draftProblems, IOptions<StudioOptions> options, ILogger<ValidateAgentFilesHandler> logger)
    : IRequestHandler<ValidateAgentFilesQuery, Outcome<AgentFilesCheck>>
{
    public async Task<Outcome<AgentFilesCheck>> Handle(ValidateAgentFilesQuery query, CancellationToken ct)
    {
        var o = options.Value;
        var (shape, problem) = AgentManifest.Read(query.Name, query.AgentYaml, query.GrantsYaml, o.Namespace);
        if (shape is null)
            return Outcome<AgentFilesCheck>.Ok(new AgentFilesCheck(false, [problem ?? "Not a studio agent."], []));

        IReadOnlyDictionary<string, string> pluginServers = new Dictionary<string, string>();
        IReadOnlyList<string> ownServers = [];
        var notes = new List<string>();
        if (o.Configured)
        {
            try
            {
                var tree = await plugins.ReadAsync(await repo.ListMainAsync(ct), o.Git.Branch, log: false, ct);
                pluginServers = tree.ServerPlugins;
                if (tree.OwnerOf(query.Name) is { } owner)
                {
                    ownServers = StudioReconciler.OwnServers(tree, query.Name);
                    notes.Add($"An agent of the plugin '{owner}': it changes with its plugin.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
            {
                logger.LogWarning("Studio CI check of {Agent}: the agent repo did not answer ({Error})", query.Name, ex.GetType().Name);
                return Outcome<AgentFilesCheck>.Unavailable(StudioFailure.Unavailable);
            }
        }
        else
            notes.Add("The studio has no agent repo configured: plugin servers were not considered.");

        List<string> problems = [.. DraftRules.Check(shape, o, pluginServers, ownServers), .. draftProblems.Licensing(shape)];
        return Outcome<AgentFilesCheck>.Ok(new AgentFilesCheck(problems.Count == 0, problems, notes));
    }
}
