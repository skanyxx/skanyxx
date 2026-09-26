using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Pipelines;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

internal sealed class DeletePipelineEndpoint(IMediator mediator) : Endpoint<PipelineRouteRequest>
{
    public override void Configure()
    {
        Delete("pipelines/{id}");
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(PipelineRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new DeletePipelineCommand(TicketsHeaders.User(HttpContext), req.Id), ct);
        await Send.ResultAsync(outcome.ToHttp(p => p));
    }
}
