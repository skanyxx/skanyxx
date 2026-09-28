using Skanyxx.Core.Platform;
using FastEndpoints;
using MediatR;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

internal sealed class UpsertCardEndpoint(IMediator mediator) : Endpoint<UpsertCardRequest>
{
    public override void Configure()
    {
        Put("cards/{scope}/{key}");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(UpsertCardRequest req, CancellationToken ct)
    {
        var command = new UpsertCardCommand(
            HumanCaller.From(User), req.Scope, req.Key, req.Version, req.Type, req.What, req.Why, req.Body, req.Source);
        var outcome = await mediator.Send(command, ct);
        await Send.ResultAsync(outcome.ToHttp(CardMapper.ToDto));
    }
}
