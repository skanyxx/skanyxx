using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Features.Runs;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

internal sealed class StartRunEndpoint(IMediator mediator) : Endpoint<StartRunRequest>
{
    public override void Configure()
    {
        Post("runs");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(StartRunRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new StartRunCommand(Caller.UserId(User), req.TicketKey, req.PipelineId), ct);
        await Send.ResultAsync(outcome.ToHttp(RunMapper.ToDto));
    }
}
