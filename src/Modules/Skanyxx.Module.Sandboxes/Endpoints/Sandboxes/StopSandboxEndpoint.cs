using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Sandboxes;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

internal sealed class StopSandboxEndpoint(IMediator mediator) : Endpoint<SandboxRouteRequest>
{
    public override void Configure()
    {
        Post("{name}/stop");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(SandboxRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new StopSandboxCommand(Caller.UserId(User), Caller.IsSupervisor(User), req.Name), ct);
        await Send.ResultAsync(outcome.ToHttp(sandbox => sandbox));
    }
}
