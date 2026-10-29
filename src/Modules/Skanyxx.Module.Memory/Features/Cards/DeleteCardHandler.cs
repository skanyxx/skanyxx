using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Coordination;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// D507: a card and its version history go for good; a card lifted from it keeps its copy (its link is cleared). Only
/// the person whose personal scope it is, or the owner for a shared scope (company, team, department): a shared card is
/// everyone's, so a writer there marks it stale instead. Nobody else's personal card, not even the owner's to delete:
/// they may not read it, and the answer (409 with the card, 404) would show it. A stored body's object stays (D182: the app never deletes objects; a lifted copy may share it).
/// </summary>
internal sealed class DeleteCardHandler(MemoryDbContext db, SearchCache cache, ClientAddress client, ILogger<DeleteCardHandler> logger)
    : IRequestHandler<DeleteCardCommand, Outcome<CardDto>>
{
    public const string Refusal = "Only your own personal cards can be deleted, and the owner deletes shared ones. Mark it stale instead.";

    public async Task<Outcome<CardDto>> Handle(DeleteCardCommand command, CancellationToken ct)
    {
        var scope = Scope.Parse(command.Scope);
        var mine = command.User.UserId is { } me && scope == Scope.Personal(me);
        if (!mine && !(command.User.IsOwner && scope.Level != ScopeLevel.Personal))
            return Outcome<CardDto>.Forbidden(Refusal);

        var card = await db.Cards.SingleOrDefaultAsync(c => c.Scope == command.Scope && c.Key == command.Key, ct);
        if (card is null)
            return Outcome<CardDto>.NotFound($"No card '{command.Scope}/{command.Key}'.");
        var dto = card.ToDto();
        if (card.Version != command.Version)
            return Outcome<CardDto>.Conflict(dto, $"Stale version {command.Version}; the card is at version {card.Version}.");

        db.Cards.Remove(card);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Outcome<CardDto>.Conflict(null, $"Stale version {command.Version}; another writer got there first.");
        }

        await cache.InvalidateAsync(command.Scope);
        logger.LogWarning("Card {Scope}/{Key} (version {Version}) deleted by {ActorUserId} from {RemoteIp}",
            card.Scope, card.Key, card.Version, command.User.UserId, client.Current);
        return Outcome<CardDto>.Ok(dto);
    }
}
