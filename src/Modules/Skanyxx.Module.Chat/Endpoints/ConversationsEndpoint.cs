using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>The caller's own conversations, newest first (the sessions sidebar).</summary>
internal sealed class ConversationsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("api/chat/conversations");
        Options(ChatRoutes.Guard());
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListConversationsQuery(Caller.UserId(User)!), ct)).ToHttp());
}
