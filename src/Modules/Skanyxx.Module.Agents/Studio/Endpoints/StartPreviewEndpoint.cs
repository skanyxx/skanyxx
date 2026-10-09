using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

internal sealed class StartPreviewEndpoint(IMediator mediator) : Endpoint<ProposalRoute>
{
    public override void Configure() => Post("api/studio/proposals/{number}/preview");

    public override async Task HandleAsync(ProposalRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new StartPreviewCommand(StudioUser.From(User), req.Number), ct)).ToHttp());
}
