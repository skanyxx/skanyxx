using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// An agent the caller may not read is "not found", as one that does not exist (no probing by name). The details carry
/// its tools with kagent's descriptions and, for builders and up, the agents that call it as a tool.
/// </summary>
internal sealed class GetAgentHandler(KAgentApiClient kagent, IOptions<StudioOptions> options, ILogger<GetAgentHandler> logger) : IRequestHandler<GetAgentQuery, Outcome<AgentDetail>>
{
    public async Task<Outcome<AgentDetail>> Handle(GetAgentQuery query, CancellationToken ct)
    {
        if (!AgentQueries.IsRef(query.Namespace, query.Name))
            return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
        try
        {
            if (await kagent.GetAgentObjectAsync(query.Namespace, query.Name, ct) is not { } agent)
                return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
            var readAll = AgentAccess.CanReadAll(query.User);
            var detail = AgentSpec.Detail(agent, readAll, AgentAccess.CanWrite(query.User) && query.Namespace == options.Value.Namespace);
            if (!AgentAccess.CanRead(query.User, detail.InChat, detail.Suspended))
                return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
            var catalog = await AgentQueries.ToolCatalogAsync(kagent, ct);
            return Outcome<AgentDetail>.Ok(detail with
            {
                ToolsByServer = [.. detail.McpTools.Select(t => AgentQueries.Describe(t, query.Namespace, catalog))],
                UsedBy = readAll ? AgentQueries.UsedBy(await kagent.GetAgentsAsync(ct), query.Namespace, query.Name) : []
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent {Namespace}/{Name}: kagent did not answer", query.Namespace, query.Name);
            return Outcome<AgentDetail>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
