using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// What the Plugins page shows: each plugin in main, its servers' state (deployed, starting, needs a secret…), its agents
/// as kagent runs them (live only once every plugin server they use is deployed, with any tool name the server does not
/// offer flagged), and the open plugin proposals. Agents come from kagent's one agent list, never one git read each.
/// </summary>
internal sealed class PluginsOverviewHandler(
    IAgentRepo repo, StudioRepoGuard guard, StudioReconciler reconciler, PluginRepoReader plugins, KAgentApiClient kagent, IOptions<StudioOptions> options,
    IHostEnvironment environment, IConfiguration configuration, ILogger<PluginsOverviewHandler> logger)
    : IRequestHandler<PluginsOverviewQuery, Outcome<PluginsOverview>>
{
    public async Task<Outcome<PluginsOverview>> Handle(PluginsOverviewQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<PluginsOverview>.Forbidden(StudioFailure.NotStudio);
        if (!options.Value.Configured)
            return Outcome<PluginsOverview>.Ok(new PluginsOverview(null, StudioFailure.NotConfigured, [], []));
        var (ns, branch) = (options.Value.Namespace, options.Value.Git.Branch);
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var open = (await repo.OpenPullsAsync(ct)).Where(p => ProposalTrailer.PluginOf(p.Body) is not null)
                .Select(p => new ProposalSummary(p.Number, p.Title, null, ProposalTrailer.ProposedBy(p.Body), p.HeadSha, p.Mergeable, ProposalTrailer.PluginOf(p.Body)))
                .ToList();
            var tree = await plugins.ReadAsync(await repo.ListMainAsync(ct), branch, log: false, ct);
            var records = await reconciler.PluginRecordsAsync(ct);
            var tools = await reconciler.ToolServerToolsAsync(ct);
            var running = (await kagent.GetAgentsAsync(ct))
                .Where(a => a.Namespace == ns && a.Labels.GetValueOrDefault(StudioNames.MergedLabel) == "true"
                    && a.Labels.GetValueOrDefault(StudioNames.ManagedByLabel) == StudioNames.ManagedBy)
                .ToDictionary(a => a.Name);

            var summaries = new List<PluginSummary>();
            foreach (var plugin in tree.Plugins.Values.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var servers = plugin.Servers.Select(s => Server(plugin, s, tree, records.GetValueOrDefault(s), tools.GetValueOrDefault($"{ns}/{s}"), ns)).ToList();
                var states = servers.ToDictionary(s => s.Name, s => s.State);
                var agents = plugin.Agents.Select(a => running.GetValueOrDefault(a) is { } agent
                    ? new StudioAgentSummary(a, agent.Description, agent.McpToolNames.Keys.Where(states.ContainsKey).All(s => states[s] == PluginServerStates.Deployed))
                    : new StudioAgentSummary(a, "", false)).ToList();
                summaries.Add(new PluginSummary(plugin.Name, plugin.Version, plugin.Description, plugin.Source, servers, agents, plugin.Notes, null)
                {
                    Warnings = [.. plugin.Agents.Select(running.GetValueOrDefault).OfType<Agent>().SelectMany(a => UnknownTools(a, states, tools, ns))]
                });
            }
            summaries.AddRange(tree.Problems.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new PluginSummary(p.Key, null, "", null, [], [], [], p.Value)));
            return Outcome<PluginsOverview>.Ok(new PluginsOverview(repo.Name, null, summaries, open) { KeyRingInClear = PluginValueKeyRing.InClear(environment, configuration) });
        }
        catch (StudioApplyException ex)
        {
            return Outcome<PluginsOverview>.Ok(new PluginsOverview(repo.Name, ex.Message, [], []));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Plugins overview failed");
            return Outcome<PluginsOverview>.Ok(new PluginsOverview(repo.Name, StudioFailure.Unavailable, [], []));
        }
    }

    /// <param name="tools">What kagent discovered on the server; null when kagent does not list it.</param>
    private static PluginServerSummary Server(InstalledPlugin plugin, string name, RepoPlugins tree, PluginServerRecord? record, IReadOnlyList<string>? tools, string ns)
    {
        var entry = tree.Servers.GetValueOrDefault(name);
        // Values entered for another address are never sent to this one (PluginServerStatus.Of), so they do not fill a header.
        var stored = record is not null && record.Plugin == plugin.Name && (entry is null || record.ValueAddress == PluginServerUrl.Address(entry.Server.Url))
            ? record.ValueNames
            : [];
        var headers = plugin.Headers.Where(h => h.Server == name)
            .Select(h => new PluginHeaderSummary(h.Name, h.Template, stored.Contains(h.Name, StringComparer.OrdinalIgnoreCase))).ToList();
        if (entry is null)
            return new PluginServerSummary(name, "", "", PluginServerStates.Invalid, headers);
        var state = PluginServerStatus.Of(entry, record, tools is not null, ns);
        // Created, but kagent has no tools from it yet: until then its agents cannot call anything.
        return new PluginServerSummary(name, entry.Server.Url, entry.Server.Protocol,
            state == PluginServerStates.Deployed && tools!.Count == 0 ? PluginServerStates.Starting : state, headers);
    }

    /// <summary>Tool names an agent asks a deployed plugin server for that the server does not offer (kagent gives the agent none of them).</summary>
    private static IEnumerable<string> UnknownTools(Agent agent, Dictionary<string, string> states, Dictionary<string, IReadOnlyList<string>> tools, string ns) =>
        agent.McpToolNames.Where(t => states.GetValueOrDefault(t.Key) == PluginServerStates.Deployed)
            .Select(t => (Server: t.Key, Unknown: t.Value.Except(tools[$"{ns}/{t.Key}"]).ToList()))
            .Where(t => t.Unknown.Count > 0)
            .Select(t => $"The agent '{agent.Name}' names tools that {t.Server} does not offer: {string.Join(", ", t.Unknown)} "
                + $"(it offers {string.Join(", ", tools[$"{ns}/{t.Server}"])}).");
}
