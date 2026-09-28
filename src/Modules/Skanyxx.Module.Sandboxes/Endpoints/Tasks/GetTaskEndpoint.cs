using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Tasks;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

internal sealed class GetTaskEndpoint(IMediator mediator) : Endpoint<TaskRouteRequest>
{
    public override void Configure()
    {
        Get("tasks/{name}");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(TaskRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetTaskQuery(Caller.UserId(User), req.Name), ct);
        await Send.ResultAsync(outcome.ToHttp(task => task));
    }
}
