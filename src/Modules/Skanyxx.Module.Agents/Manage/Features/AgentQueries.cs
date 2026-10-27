using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>The kagent reads a write needs: the models and agents of the namespace.</summary>
internal static class AgentQueries
{
    public static async Task<IReadOnlyList<string>> ModelsAsync(KAgentApiClient kagent, string ns, CancellationToken ct) =>
        [.. (await kagent.GetModelConfigRefsAsync(ct)).Where(r => r.StartsWith(ns + "/", StringComparison.Ordinal)).Select(r => r[(ns.Length + 1)..])];

    public static async Task<IReadOnlyList<string>> AgentsAsync(KAgentApiClient kagent, string ns, CancellationToken ct) =>
        [.. (await kagent.GetAgentsAsync(ct)).Where(a => a.Namespace == ns).Select(a => a.Name).Order(StringComparer.Ordinal)];

    /// <summary>A route's namespace and name, both Kubernetes names; anything else is no agent at all.</summary>
    public static bool IsRef(string ns, string name) => StudioNames.IsKubeName(ns) && StudioNames.IsKubeName(name);
}
