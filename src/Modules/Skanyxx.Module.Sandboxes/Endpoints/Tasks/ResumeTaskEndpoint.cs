using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Tasks;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

internal sealed class ResumeTaskEndpoint(IMediator mediator) : Endpoint<TaskRouteRequest>
{
    public override void Configure()
    {
        Post("tasks/{name}/resume");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(TaskRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SetTaskSuspendedCommand(SandboxesHeaders.User(HttpContext), req.Name, Suspend: false), ct);
        await Send.ResultAsync(outcome.ToHttp(task => task));
    }
}
