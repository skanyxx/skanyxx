using System.Text.Json.Nodes;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>The kagent reads a write needs: the models and agents of the namespace.</summary>
internal static class AgentQueries
{
    public static async Task<IReadOnlyList<string>> ModelsAsync(KAgentApiClient kagent, string ns, CancellationToken ct) =>
        [.. (await kagent.GetModelConfigRefsAsync(ct)).Where(r => r.StartsWith(ns + "/", StringComparison.Ordinal)).Select(r => r[(ns.Length + 1)..])];

    public static async Task<IReadOnlyList<Agent>> AgentsAsync(KAgentApiClient kagent, string ns, CancellationToken ct) =>
        [.. (await kagent.GetAgentsAsync(ct)).Where(a => a.Namespace == ns).OrderBy(a => a.Name, StringComparer.Ordinal)];

    /// <summary>The agents an agent may call as tools (<see cref="AgentProtection.IsCallable"/>).</summary>
    public static IReadOnlyList<string> Callable(IEnumerable<Agent> agents) =>
        [.. agents.Where(a => AgentProtection.IsCallable(a.Name, a.Labels)).Select(a => a.Name)];

    /// <summary>A route's namespace and name, both Kubernetes names; anything else is no agent at all.</summary>
    public static bool IsRef(string ns, string name) => StudioNames.IsKubeName(ns) && StudioNames.IsKubeName(name);

    /// <summary>kagent's tool servers (<c>ns/name</c>) with the tools and descriptions it discovered; the first entry of a repeated ref wins.</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<ToolInfo>>> ToolCatalogAsync(KAgentApiClient kagent, CancellationToken ct) =>
        ToolCatalog(await kagent.GetToolServerListAsync(ct));

    /// <inheritdoc cref="ToolCatalogAsync"/>
    public static IReadOnlyDictionary<string, IReadOnlyList<ToolInfo>> ToolCatalog(JsonArray toolServers) =>
        toolServers
            .Select(s => (Ref: Text(s?["ref"]), Tools: (IReadOnlyList<ToolInfo>)[.. (s?["discoveredTools"] as JsonArray ?? [])
                .Where(t => Text(t?["name"]) is not null).Select(t => new ToolInfo(Text(t!["name"])!, Text(t["description"]) ?? ""))]))
            .Where(s => s.Ref is not null)
            .GroupBy(s => s.Ref!)
            .ToDictionary(g => g.Key, g => g.First().Tools);

    /// <summary>A server's tools as the agent names them, with the catalog's descriptions; no names = every tool the server lists.</summary>
    public static ServerTools Describe(McpToolChoice choice, string ns, IReadOnlyDictionary<string, IReadOnlyList<ToolInfo>> catalog)
    {
        var known = catalog.GetValueOrDefault($"{ns}/{choice.Server}") ?? [];
        return new ServerTools(choice.Server, choice.Tools.Count == 0
            ? known
            : [.. choice.Tools.Select(t => known.FirstOrDefault(k => k.Name == t) ?? new ToolInfo(t, ""))]);
    }

    /// <summary>The agents of <paramref name="ns"/> that call <paramref name="name"/> as a tool.</summary>
    public static IReadOnlyList<string> UsedBy(IEnumerable<Agent> agents, string ns, string name) =>
        [.. agents.Where(a => a.Namespace == ns && a.AgentToolNames.Contains(name)).Select(a => a.Name).Order(StringComparer.Ordinal)];

    /// <summary>As kagent-ui counts them: each named MCP tool (a server with none counts once) and each agent tool.</summary>
    public static int ToolCount(Agent agent) =>
        agent.McpToolNames.Values.Sum(t => Math.Max(1, t.Count)) + agent.AgentToolNames.Count;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
