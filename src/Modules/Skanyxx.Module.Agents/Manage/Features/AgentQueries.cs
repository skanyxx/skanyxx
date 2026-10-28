using Skanyxx.Core.Models;
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
}
