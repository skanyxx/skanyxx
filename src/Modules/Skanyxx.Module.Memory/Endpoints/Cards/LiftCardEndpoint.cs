using Skanyxx.Core.Platform;
using FastEndpoints;
using MediatR;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

internal sealed class LiftCardEndpoint(IMediator mediator) : Endpoint<LiftCardRequest>
{
    public override void Configure()
    {
        Post("cards/{scope}/{key}/lift");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(LiftCardRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(
            new LiftCardCommand(MemoryHeaders.Human(HttpContext), req.Scope, req.Key, req.TargetScope), ct);
        await Send.ResultAsync(outcome.ToHttp(CardMapper.ToDto));
    }
}
