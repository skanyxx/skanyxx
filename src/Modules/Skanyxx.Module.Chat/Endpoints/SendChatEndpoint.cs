using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>One message to a merged agent, as the signed-in person (never a user id from the body).</summary>
internal sealed class SendChatEndpoint(IMediator mediator) : Endpoint<SendChatRequest>
{
    public const int MaxBodyBytes = 64 * 1024;

    public override void Configure()
    {
        Post("api/chat");
        Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes)));
    }

    public override async Task HandleAsync(SendChatRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SendChatMessageCommand(
            Caller.UserId(User)!, req.Message ?? "", req.AgentNamespace?.Trim() ?? "", req.AgentName?.Trim() ?? "", req.ConversationId), ct);
        await Send.ResultAsync(outcome.ToHttp(r => r));
    }
}
