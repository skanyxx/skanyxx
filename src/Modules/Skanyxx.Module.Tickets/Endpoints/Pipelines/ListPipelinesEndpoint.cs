using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Features.Pipelines;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

internal sealed class ListPipelinesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("pipelines");
        // D394: runs and pipelines hold Jira snapshots, prompts and answers: the Tickets panel is supervisors' (D392).
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        Group<TicketsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListPipelinesQuery(Caller.UserId(User)), ct);
        await Send.ResultAsync(outcome.ToHttp(p => p));
    }
}
