using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>An agent the caller may not read is "not found", as one that does not exist (no probing by name).</summary>
internal sealed class GetAgentHandler(KAgentApiClient kagent, ILogger<GetAgentHandler> logger) : IRequestHandler<GetAgentQuery, Outcome<AgentDetail>>
{
    public async Task<Outcome<AgentDetail>> Handle(GetAgentQuery query, CancellationToken ct)
    {
        if (!AgentQueries.IsRef(query.Namespace, query.Name))
            return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
        System.Text.Json.Nodes.JsonObject? agent;
        try
        {
            agent = await kagent.GetAgentObjectAsync(query.Namespace, query.Name, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent {Namespace}/{Name}: kagent did not answer", query.Namespace, query.Name);
            return Outcome<AgentDetail>.Unavailable(AgentFailure.Unavailable);
        }
        if (agent is null)
            return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
        var detail = AgentSpec.Detail(agent, AgentAccess.CanReadAll(query.User), AgentAccess.CanWrite(query.User));
        return AgentAccess.CanRead(query.User, detail.InChat) ? Outcome<AgentDetail>.Ok(detail) : Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
    }
}
