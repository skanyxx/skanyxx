using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Sandboxes;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

internal sealed class ListSandboxesEndpoint(IMediator mediator) : Endpoint<ListSandboxesRequest>
{
    public override void Configure()
    {
        Get("/" + SandboxesGroup.Prefix);
        SandboxesGroup.Apply(Definition);
    }

    public override async Task HandleAsync(ListSandboxesRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListSandboxesQuery(Caller.UserId(User), req.Limit, req.Offset), ct);
        await Send.ResultAsync(outcome.ToHttp(sandboxes => sandboxes));
    }
}
