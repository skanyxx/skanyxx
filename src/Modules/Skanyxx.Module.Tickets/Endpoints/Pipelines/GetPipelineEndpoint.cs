using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Pipelines;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

internal sealed class GetPipelineEndpoint(IMediator mediator) : Endpoint<PipelineRouteRequest>
{
    public override void Configure()
    {
        Get("pipelines/{id}");
        // D394: runs and pipelines hold Jira snapshots, prompts and answers: the Tickets panel is supervisors' (D392).
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(PipelineRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetPipelineQuery(Caller.UserId(User), req.Id), ct);
        await Send.ResultAsync(outcome.ToHttp(p => p));
    }
}
