using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Tasks;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

internal sealed class ListTasksEndpoint(IMediator mediator) : Endpoint<ListTasksRequest>
{
    public override void Configure()
    {
        Get("tasks");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(ListTasksRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListTasksQuery(Caller.UserId(User), req.Limit, req.Offset), ct);
        await Send.ResultAsync(outcome.ToHttp(tasks => tasks));
    }
}
