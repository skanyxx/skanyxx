using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Workspaces;

namespace Skanyxx.Module.Sandboxes.Endpoints.Workspaces;

internal sealed class ListWorkspacesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("workspaces");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListWorkspacesQuery(Caller.UserId(User)), ct);
        await Send.ResultAsync(outcome.ToHttp(workspaces => workspaces));
    }
}
