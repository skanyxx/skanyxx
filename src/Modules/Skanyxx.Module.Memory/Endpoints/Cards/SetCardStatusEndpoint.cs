using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

/// <summary>Publish a candidate, mark a card stale (optionally superseded by another), or bring it back.</summary>
internal sealed class SetCardStatusEndpoint(IMediator mediator) : Endpoint<SetCardStatusRequest>
{
    public override void Configure()
    {
        Post("cards/{scope}/{key}/status");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(SetCardStatusRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SetCardStatusCommand(
            LibraryUser.From(User), req.Scope, req.Key, req.Version, req.Status, req.SupersededBy), ct);
        await Send.ResultAsync(outcome.ToHttp());
    }
}
