using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Features;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class TicketViewEndpoint(IMediator mediator) : Endpoint<TicketRouteRequest>
{
    public override void Configure()
    {
        Get("tickets/{key}");
        Group<ChatIndexGroup>();
    }

    public override async Task HandleAsync(TicketRouteRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new TicketViewQuery(ChatReader.From(User), req.Key), ct)).ToHttp());
}
