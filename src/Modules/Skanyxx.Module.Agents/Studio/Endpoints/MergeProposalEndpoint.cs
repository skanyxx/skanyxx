using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Supervisor only (D024): <c>200</c> live, <c>202</c> merged but kagent not there yet, <c>403</c> a builder.</summary>
internal sealed class MergeProposalEndpoint(IMediator mediator) : Endpoint<ProposalRoute>
{
    public override void Configure() => Post("api/studio/proposals/{number}/merge");

    public override async Task HandleAsync(ProposalRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new MergeProposalCommand(StudioUser.From(User), req.Number), ct)).ToHttp());
}
