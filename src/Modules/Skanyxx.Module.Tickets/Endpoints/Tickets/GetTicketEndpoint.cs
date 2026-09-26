using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Tickets;

namespace Skanyxx.Module.Tickets.Endpoints.Tickets;

internal sealed class GetTicketEndpoint(IMediator mediator) : Endpoint<TicketRouteRequest>
{
    public override void Configure()
    {
        Get("issues/{key}");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(TicketRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetTicketQuery(TicketsHeaders.User(HttpContext), req.Key), ct);
        await Send.ResultAsync(outcome.ToHttp(t => t));
    }
}
