using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>The agent details panel; <c>404</c> for any agent Chat does not offer.</summary>
internal sealed class ChatAgentDetailsEndpoint(IMediator mediator) : Endpoint<AgentRouteRequest>
{
    public override void Configure()
    {
        Get("api/chat/agents/{namespace}/{name}");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(AgentRouteRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ChatAgentDetailsQuery(req.Namespace, req.Name, Caller.SeesAgentInternals(User)), ct)).ToHttp());
}
