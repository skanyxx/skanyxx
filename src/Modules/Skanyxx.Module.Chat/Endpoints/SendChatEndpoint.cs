using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>One blocking message to an agent Chat offers (D557), as the signed-in person (the API; the page streams).</summary>
internal sealed class SendChatEndpoint(IMediator mediator) : Endpoint<SendChatRequest>
{
    public const int MaxBodyBytes = 64 * 1024;

    public override void Configure()
    {
        Post("api/chat");
        Options(ChatRoutes.Guard(MaxBodyBytes));
    }

    public override async Task HandleAsync(SendChatRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(req.Command(User), ct)).ToHttp(r => r));
}
