using FastEndpoints;
using MediatR;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

internal sealed class SearchCardsEndpoint(IMediator mediator) : Endpoint<SearchCardsRequest>
{
    public override void Configure()
    {
        Get("cards");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(SearchCardsRequest req, CancellationToken ct)
    {
        var hits = await mediator.Send(new SearchCardsQuery(MemoryHeaders.Human(HttpContext), req.Q), ct);
        await Send.OkAsync(hits, ct);
    }
}
