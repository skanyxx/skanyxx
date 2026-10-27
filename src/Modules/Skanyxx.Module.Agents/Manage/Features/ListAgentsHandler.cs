using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>Every agent for builders and supervisors; the agents Chat offers for anyone else (D254).</summary>
internal sealed class ListAgentsHandler(KAgentApiClient kagent, IOptions<StudioOptions> options, ILogger<ListAgentsHandler> logger)
    : IRequestHandler<ListAgentsQuery, Outcome<IReadOnlyList<AgentSummary>>>
{
    public async Task<Outcome<IReadOnlyList<AgentSummary>>> Handle(ListAgentsQuery query, CancellationToken ct)
    {
        List<Core.Models.Agent> agents;
        try
        {
            agents = await kagent.GetAgentsAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agents list: kagent did not answer");
            return Outcome<IReadOnlyList<AgentSummary>>.Unavailable(AgentFailure.Unavailable);
        }
        var canWrite = AgentAccess.CanWrite(query.User);
        return Outcome<IReadOnlyList<AgentSummary>>.Ok([.. agents
            .Select(a => (Agent: a, InChat: a.Labels.GetValueOrDefault(StudioNames.MergedLabel) == "true"))
            .Where(a => AgentAccess.CanRead(query.User, a.InChat))
            .OrderBy(a => a.Agent.Namespace, StringComparer.Ordinal).ThenBy(a => a.Agent.Name, StringComparer.Ordinal)
            .Select(a =>
            {
                var refusal = AgentProtection.Refusal(a.Agent.Name, a.Agent.Labels);
                var writable = canWrite && refusal is null && a.Agent.Namespace == options.Value.Namespace;
                // The details decide finally (a prompt from a ConfigMap, tools in another namespace).
                return new AgentSummary(a.Agent.Namespace, a.Agent.Name, a.Agent.Description, a.Agent.Type, a.Agent.Ready, a.Agent.Accepted,
                    AgentProtection.SourceOf(a.Agent.Name, a.Agent.Labels), a.InChat, a.Agent.Suspended,
                    writable && a.Agent.Type == "Declarative", writable, refusal,
                    [.. a.Agent.McpToolNames.Keys.Order(StringComparer.Ordinal)]);
            })]);
    }
}
