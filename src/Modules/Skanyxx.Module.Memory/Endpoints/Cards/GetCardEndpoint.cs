using Skanyxx.Core.Platform;
using FastEndpoints;
using MediatR;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

internal sealed class GetCardEndpoint(IMediator mediator) : Endpoint<CardRouteRequest>
{
    public override void Configure()
    {
        Get("cards/{scope}/{key}");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(CardRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetCardQuery(MemoryHeaders.Human(HttpContext), req.Scope, req.Key), ct);
        await Send.ResultAsync(outcome.ToHttp(CardMapper.ToDto));
    }
}
