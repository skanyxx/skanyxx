using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Tasks;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

internal sealed class StopTaskEndpoint(IMediator mediator) : Endpoint<TaskRouteRequest>
{
    public override void Configure()
    {
        Post("tasks/{name}/stop");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(TaskRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new StopTaskCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Name), ct);
        await Send.ResultAsync(outcome.ToHttp(task => task));
    }
}
