using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Tasks;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

internal sealed class RunTaskEndpoint(IMediator mediator) : Endpoint<RunTaskRequest>
{
    public override void Configure()
    {
        Put("tasks/{name}");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(RunTaskRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new RunTaskCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Name, req.Image,
            req.Command ?? [], req.Env ?? [], req.Workspaces ?? [], req.Resources), ct);
        await Send.ResultAsync(outcome.ToHttp(task => task));
    }
}
