using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Owner only (D119): trust the repo git has now, and reconcile once without the removal brake.</summary>
internal sealed class ConfirmRepoEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure() => Post("api/studio/confirm");

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ConfirmRepoCommand(StudioUser.From(User)), ct)).ToHttp());
}
