using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>The signed-in person's own conversation, for the Chat page to show again after a reload.</summary>
internal sealed class ConversationEventsEndpoint(IMediator mediator) : Endpoint<ConversationEventsRequest>
{
    public override void Configure()
    {
        Get("api/chat/conversations/{id}/events");
        Options(b => b.WithMetadata(new DisableCorsAttribute()));
    }

    public override async Task HandleAsync(ConversationEventsRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ConversationEventsQuery(Caller.UserId(User)!, req.Id), ct)).ToHttp(e => e));
}
