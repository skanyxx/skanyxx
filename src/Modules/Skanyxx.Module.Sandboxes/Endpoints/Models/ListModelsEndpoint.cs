using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Models;

namespace Skanyxx.Module.Sandboxes.Endpoints.Models;

internal sealed class ListModelsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("models");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var outcome = await mediator.Send(new ListModelsQuery(SandboxesHeaders.User(HttpContext)), ct);
        await Send.ResultAsync(outcome.ToHttp(models => models));
    }
}
