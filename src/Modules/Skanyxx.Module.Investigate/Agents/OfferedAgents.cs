using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// The agents Chat offers (<see cref="ChatOffer"/>, D557): every agent kagent lists except Skanyxx's own machinery
/// (Studio previews, the factory, ticket stages) and suspended ones. Nothing else is ever a step.
/// </summary>
internal sealed class OfferedAgents(KAgentApiClient kagent) : IInvestigationAgents
{
    public async Task<IReadOnlyList<AgentChoice>> ListAsync(CancellationToken ct) =>
        [.. ChatOffer.Offered(await kagent.GetAgentsAsync(ct))
            .OrderBy(a => a.Namespace, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => new AgentChoice($"{a.Namespace}/{a.Name}", a.Namespace, a.Name, a.Description, a.Type == "Declarative",
                [.. a.McpToolNames.Keys], a.AgentToolNames.Count > 0, a.ModelConfig))];
}
