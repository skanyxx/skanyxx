using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>The template gallery (D360).</summary>
internal sealed class StudioTemplatesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure() => Get("api/studio/templates");

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new StudioTemplatesQuery(StudioUser.From(User)), ct)).ToHttp());
}
