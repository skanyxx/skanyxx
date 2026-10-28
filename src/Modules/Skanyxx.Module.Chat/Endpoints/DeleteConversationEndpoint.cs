using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class DeleteConversationEndpoint(IMediator mediator) : Endpoint<ConversationRouteRequest>
{
    public override void Configure()
    {
        Delete("api/chat/conversations/{id}");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(ConversationRouteRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new DeleteConversationCommand(Caller.UserId(User)!, req.Id), ct)).ToHttp());
}
