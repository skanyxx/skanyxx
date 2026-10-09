using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>Save → PR (D028): <c>201</c> with the proposal's number; nothing is applied to kagent.</summary>
internal sealed class ProposeAgentEndpoint(IMediator mediator) : Endpoint<AgentDraft>
{
    public override void Configure()
    {
        Post("api/studio/proposals");
        Options(b => b.WithMetadata(new RequestSizeLimitAttribute(64 * 1024)));
    }

    public override async Task HandleAsync(AgentDraft req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ProposeAgentCommand(StudioUser.From(User), req.Normalized()), ct)).ToHttp());
}
