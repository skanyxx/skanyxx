using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Features;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class ShareSessionEndpoint(IMediator mediator) : Endpoint<ShareSessionRequest>
{
    public override void Configure()
    {
        Put("sessions/{id}/scope");
        Group<ChatIndexGroup>();
    }

    public override async Task HandleAsync(ShareSessionRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ShareSessionCommand(ChatReader.From(User), req.Id, req.Scope ?? ""), ct)).ToHttp());
}
