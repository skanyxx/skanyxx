using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Tickets;

namespace Skanyxx.Module.Tickets.Endpoints.Tickets;

internal sealed class ListTicketsEndpoint(IMediator mediator) : Endpoint<ListTicketsRequest>
{
    public override void Configure()
    {
        Get("issues");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(ListTicketsRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListTicketsQuery(TicketsHeaders.User(HttpContext), req.Assignee, req.Limit), ct);
        await Send.ResultAsync(outcome.ToHttp(t => t));
    }
}
