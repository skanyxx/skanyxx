using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Sandboxes;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

internal sealed class RunSandboxEndpoint(IMediator mediator) : Endpoint<RunSandboxRequest>
{
    public override void Configure()
    {
        Put("{name}");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(RunSandboxRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new RunSandboxCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Name, req.Image,
            req.Command ?? [], req.Env ?? [], req.Workspaces ?? [], req.Resources), ct);
        await Send.ResultAsync(outcome.ToHttp(sandbox => sandbox));
    }
}
