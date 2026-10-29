using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Owner only (D362): the CI workflow into the agent repo, as one pull request merged at once.</summary>
internal sealed class InstallCiEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure() => Post("api/studio/ci/install");

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new InstallCiCommand(StudioUser.From(User)), ct)).ToHttp());
}
