using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Sandboxes;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

internal sealed class ResumeSandboxEndpoint(IMediator mediator) : Endpoint<SandboxRouteRequest>
{
    public override void Configure()
    {
        Post("{name}/resume");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(SandboxRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SetSandboxSuspendedCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Name, Suspend: false), ct);
        await Send.ResultAsync(outcome.ToHttp(sandbox => sandbox));
    }
}
