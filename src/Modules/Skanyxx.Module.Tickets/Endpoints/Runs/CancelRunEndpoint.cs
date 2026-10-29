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
        // D394: every Tickets route is a supervisor's; a demoted starter's runs are cancelled for them (CancelRunsOnPrivilegesRevoked).
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(RunRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new CancelRunCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Id), ct);
        await Send.ResultAsync(outcome.ToHttp(RunMapper.ToDto));
    }
}
