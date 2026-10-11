using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Sandboxes;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

internal sealed class GetSandboxEndpoint(IMediator mediator) : Endpoint<SandboxRouteRequest>
{
    public override void Configure()
    {
        Get("{name}");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(SandboxRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetSandboxQuery(Caller.UserId(User), req.Name), ct);
        await Send.ResultAsync(outcome.ToHttp(sandbox => sandbox));
    }
}
