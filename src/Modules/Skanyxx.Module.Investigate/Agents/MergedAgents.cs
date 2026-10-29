using Skanyxx.Core.Services;

namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// The agents people may talk to in Chat (D4, D257): Agent CRs labelled <c>skanyxx.dev/merged: "true"</c> and not
/// suspended. Everything else in kagent (ticket stages, kagent's own agents with cluster tools) is never a step.
/// </summary>
internal sealed class MergedAgents(KAgentApiClient kagent) : IInvestigationAgents
{
    private const string Label = "skanyxx.dev/merged";

    public async Task<IReadOnlyList<AgentChoice>> ListAsync(CancellationToken ct) =>
        [.. (await kagent.GetAgentsAsync(ct))
            .Where(a => a.Labels.TryGetValue(Label, out var merged) && merged == "true" && !a.Suspended)
            .OrderBy(a => a.Namespace, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => new AgentChoice($"{a.Namespace}/{a.Name}", a.Namespace, a.Name, a.Description))];
}
