using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

internal sealed class ProposalEndpoint(IMediator mediator) : Endpoint<ProposalRoute>
{
    public override void Configure() => Get("api/studio/proposals/{number}");

    public override async Task HandleAsync(ProposalRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ProposalQuery(StudioUser.From(User), req.Number), ct)).ToHttp());
}
