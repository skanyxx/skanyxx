using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>The agents a signed-in person may talk to (every kagent agent but Skanyxx's own, D557): the picker.</summary>
internal sealed class ChatAgentsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("api/chat/agents");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListChatAgentsQuery(Caller.SeesAgentInternals(User)), ct)).ToHttp(a => a));
}
