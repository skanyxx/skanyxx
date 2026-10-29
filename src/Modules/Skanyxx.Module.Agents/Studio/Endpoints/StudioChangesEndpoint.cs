using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Each change's timeline: pull request, checks, merge, deploy (D365).</summary>
internal sealed class StudioChangesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure() => Get("api/studio/changes");

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new StudioChangesQuery(StudioUser.From(User)), ct)).ToHttp());
}
