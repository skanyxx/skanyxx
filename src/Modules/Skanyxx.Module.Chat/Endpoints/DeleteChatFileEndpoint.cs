using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Endpoints;

internal sealed class DeleteChatFileEndpoint(IMediator mediator) : Endpoint<FileRouteRequest>
{
    public override void Configure()
    {
        Delete("api/chat/files/{id}");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(FileRouteRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new DeleteChatFileCommand(Caller.UserId(User)!, req.Id), ct)).ToHttp());
}
