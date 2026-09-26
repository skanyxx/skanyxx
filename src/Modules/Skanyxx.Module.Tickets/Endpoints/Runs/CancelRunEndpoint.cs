using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Features.Runs;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

internal sealed class CancelRunEndpoint(IMediator mediator) : Endpoint<RunRouteRequest>
{
    public override void Configure()
    {
        Post("runs/{id}/cancel");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(RunRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new CancelRunCommand(TicketsHeaders.User(HttpContext), req.Id), ct);
        await Send.ResultAsync(outcome.ToHttp(RunMapper.ToDto));
    }
}
