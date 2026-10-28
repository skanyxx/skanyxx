using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>One of the caller's conversations with its messages and tool steps; anyone else's is 404.</summary>
internal sealed class ConversationEndpoint(IMediator mediator) : Endpoint<ConversationRouteRequest>
{
    public override void Configure()
    {
        Get("api/chat/conversations/{id}");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(ConversationRouteRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetConversationQuery(Caller.UserId(User)!, req.Id), ct)).ToHttp());
}
