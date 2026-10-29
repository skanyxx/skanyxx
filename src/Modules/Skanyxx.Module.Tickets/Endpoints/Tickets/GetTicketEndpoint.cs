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
        // D392: Jira is read with Skanyxx's own token, so it is not everyone's to browse.
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(TicketRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetTicketQuery(Caller.UserId(User), req.Key), ct);
        await Send.ResultAsync(outcome.ToHttp(t => t));
    }
}
