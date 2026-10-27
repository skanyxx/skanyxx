using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Features;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class ClearAnswerCacheEndpoint(IMediator mediator) : Endpoint<ClearAnswerCacheRequest>
{
    public override void Configure()
    {
        Delete("cache");
        Group<ChatIndexGroup>();
    }

    public override async Task HandleAsync(ClearAnswerCacheRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ClearAnswerCacheCommand(ChatReader.From(User), req.All), ct)).ToHttp());
}
