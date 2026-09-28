using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Features.Runs;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

internal sealed class DecideGateEndpoint(IMediator mediator) : Endpoint<DecideGateRequest>
{
    public override void Configure()
    {
        Post("runs/{id}/decision");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(DecideGateRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new DecideGateCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Id, req.Decision, req.Note), ct);
        await Send.ResultAsync(outcome.ToHttp(RunMapper.ToDto));
    }
}
