using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// D038: a rename is a write to the card's scope (<see cref="AccessPolicy.CanUpsertAsync"/>), checked against the
/// version the person read (D041) and against the keys already in the scope. The row keeps its id, so copies lifted from
/// it keep their link. <c>who</c> stays the author of what/why; the audit line names who renamed it.
/// </summary>
internal sealed class RenameCardHandler(
    MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter, ClientAddress client, ILogger<RenameCardHandler> logger)
    : IRequestHandler<RenameCardCommand, Outcome<CardDto>>
{
    public async Task<Outcome<CardDto>> Handle(RenameCardCommand command, CancellationToken ct) =>
        (await RenameAsync(MemoryCaller.For(command.User), command, ct)).Map(CardMapper.ToDto);

    private async Task<Outcome<Card>> RenameAsync(MemoryCaller caller, RenameCardCommand command, CancellationToken ct)
    {
        var scope = Scope.Parse(command.Scope);
        if (!await access.CanUpsertAsync(caller, scope, ct))
            return Outcome<Card>.Forbidden(AccessPolicy.UpsertRefusal(caller, scope));

        var card = await db.Cards.SingleOrDefaultAsync(c => c.Scope == command.Scope && c.Key == command.Key, ct);
        if (card is null)
            return Outcome<Card>.NotFound($"No card '{scope}/{command.Key}'.");
        if (card.Version != command.Version)
            return Outcome<Card>.Conflict(
                card, $"Stale version {command.Version}; the card is at version {card.Version}.", RenameCardCommand.Stale);
        // Checked first so the usual conflict is not a failed UPDATE, which EF logs at Error; the catch is for a race.
        if (await db.Cards.AnyAsync(c => c.Scope == command.Scope && c.Key == command.NewKey, ct))
            return await TakenAsync(command, ct);
        // Only a rename that is about to be written takes a slot: a page refresh or a stale form re-submitted costs none.
        if (!limiter.TryAcquire(caller.RateLimitKey))
            return Outcome<Card>.RateLimited("Write rate limit reached; try again later.");

        card.Key = command.NewKey;
        card.Version++;
        card.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == card.Id, ct);
            return current is null
                ? Outcome<Card>.NotFound($"No card '{scope}/{command.Key}'.")
                : Outcome<Card>.Conflict(
                    current, $"Stale version {command.Version}; another writer got there first.", RenameCardCommand.Stale);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await TakenAsync(command, ct);
        }

        logger.LogWarning("Card {Scope}/{OldKey} renamed to {NewKey} (version {Version}) by {ActorUserId} from {RemoteIp}",
            scope, command.Key, card.Key, card.Version, caller.UserId, client.Current);
        return Outcome<Card>.Ok(card);
    }

    /// <summary>The card already at the new key: the caller writes this scope, so may read it.</summary>
    private async Task<Outcome<Card>> TakenAsync(RenameCardCommand command, CancellationToken ct) =>
        Outcome<Card>.Conflict(
            await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Scope == command.Scope && c.Key == command.NewKey, ct),
            $"'{command.Scope}/{command.NewKey}' already exists; pick another key.", RenameCardCommand.Taken);
}
