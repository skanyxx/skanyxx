using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Sandboxes;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

internal sealed class SuspendSandboxEndpoint(IMediator mediator) : Endpoint<SandboxRouteRequest>
{
    public override void Configure()
    {
        Post("{name}/suspend");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(SandboxRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SetSandboxSuspendedCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Name, Suspend: true), ct);
        await Send.ResultAsync(outcome.ToHttp(sandbox => sandbox));
    }
}
