using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.ChatContext.Features;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class AnswerCacheStatsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("cache");
        Group<ChatIndexGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new AnswerCacheStatsQuery(ChatReader.From(User)), ct)).ToHttp());
}
