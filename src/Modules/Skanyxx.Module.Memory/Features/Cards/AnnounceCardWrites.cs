using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// After a successful upsert, lift or rename: <see cref="CardsChanged"/> for the written scope (D265). Not for a chat or run
/// card written by extraction (D301): it only restates a chat or run, and announcing it after every turn would clear the
/// person's answer cache every time they chat.
/// </summary>
internal sealed class AnnounceCardWrites<TRequest, TResponse>(IPublisher publisher) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();
        if (request is UpsertCardCommand { EvictExtracted: true })
            return response;
        var scope = response switch
        {
            Outcome<Card> { Status: OutcomeStatus.Ok or OutcomeStatus.Created, Value: { } card } => card.Scope,
            Outcome<CardDto> { Status: OutcomeStatus.Ok or OutcomeStatus.Created, Value: { } card } => card.Scope,
            _ => null
        };
        if (scope is not null)
            await publisher.Publish(new CardsChanged(scope), cancellationToken);
        return response;
    }
}
