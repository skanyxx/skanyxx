using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

internal sealed class CloseProposalEndpoint(IMediator mediator) : Endpoint<ProposalRoute>
{
    public override void Configure() => Post("api/studio/proposals/{number}/close");

    public override async Task HandleAsync(ProposalRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new CloseProposalCommand(StudioUser.From(User), req.Number), ct)).ToHttp());
}
