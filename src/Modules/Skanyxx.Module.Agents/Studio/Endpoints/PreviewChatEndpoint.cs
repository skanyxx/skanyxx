using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Endpoints;

/// <summary>One turn with a proposal's preview, as the signed-in builder or supervisor (D030).</summary>
internal sealed class PreviewChatEndpoint(IMediator mediator) : Endpoint<PreviewChatRequest>
{
    public override void Configure()
    {
        Post("api/studio/proposals/{number}/chat");
        Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(64 * 1024)));
    }

    public override async Task HandleAsync(PreviewChatRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new PreviewChatCommand(StudioUser.From(User), req.Number, req.Message ?? "", req.ConversationId), ct)).ToHttp());
}
