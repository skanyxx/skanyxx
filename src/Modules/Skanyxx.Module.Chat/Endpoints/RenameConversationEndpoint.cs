using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class RenameConversationEndpoint(IMediator mediator) : Endpoint<RenameConversationRequest>
{
    public override void Configure()
    {
        Patch("api/chat/conversations/{id}");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(RenameConversationRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new RenameConversationCommand(Caller.UserId(User)!, req.Id, req.Title ?? ""), ct)).ToHttp());
}
