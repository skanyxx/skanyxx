using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Runs;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

internal sealed class ListRunsEndpoint(IMediator mediator) : Endpoint<ListRunsRequest>
{
    public override void Configure()
    {
        Get("runs");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(ListRunsRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListRunsQuery(Caller.UserId(User), req.TicketKey), ct);
        await Send.ResultAsync(outcome.ToHttp(runs => runs));
    }
}
