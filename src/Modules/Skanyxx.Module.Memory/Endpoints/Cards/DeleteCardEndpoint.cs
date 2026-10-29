using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

/// <summary>
/// Deletes a card for good (D507). Over the API the owner's only; a person deletes their own personal cards on the Library
/// page, which sends the same command (the security stream's role matrix decides whether the API widens).
/// </summary>
internal sealed class DeleteCardEndpoint(IMediator mediator) : Endpoint<DeleteCardRequest>
{
    public override void Configure()
    {
        Delete("cards/{scope}/{key}");
        Group<MemoryGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(DeleteCardRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new DeleteCardCommand(LibraryUser.From(User), req.Scope, req.Key, req.Version), ct);
        await Send.ResultAsync(outcome.ToHttp());
    }
}
