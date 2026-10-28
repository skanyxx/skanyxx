using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

internal sealed class CardHistoryEndpoint(IMediator mediator) : Endpoint<CardRouteRequest>
{
    public override void Configure()
    {
        Get("cards/{scope}/{key}/history");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(CardRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new CardHistoryQuery(LibraryUser.From(User), req.Scope, req.Key), ct);
        await Send.ResultAsync(outcome.ToHttp());
    }
}
