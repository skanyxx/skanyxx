using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Pipelines;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

internal sealed class SavePipelineEndpoint(IMediator mediator) : Endpoint<SavePipelineRequest>
{
    public override void Configure()
    {
        Put("pipelines/{id}");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(SavePipelineRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(
            new SavePipelineCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Id, req.Name, req.Description, req.Stages), ct);
        await Send.ResultAsync(outcome.ToHttp(p => p));
    }
}
