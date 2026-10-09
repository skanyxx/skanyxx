using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

/// <summary>D038: a person renames a card's key. REST only; the MCP tools have no rename (agents propose keys).</summary>
internal sealed class RenameCardEndpoint(IMediator mediator) : Endpoint<RenameCardRequest>
{
    public override void Configure()
    {
        Post("cards/{scope}/{key}/rename");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(RenameCardRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new RenameCardCommand(LibraryUser.From(User), req.Scope, req.Key, req.NewKey, req.Version), ct);
        await Send.ResultAsync(outcome.ToHttp());
    }
}
