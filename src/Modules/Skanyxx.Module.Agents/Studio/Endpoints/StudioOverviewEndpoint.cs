using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

internal sealed class StudioOverviewEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure() => Get("api/studio");

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new StudioOverviewQuery(StudioUser.From(User)), ct)).ToHttp());
}
