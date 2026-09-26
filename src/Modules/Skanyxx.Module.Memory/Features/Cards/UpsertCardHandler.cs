using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class UpsertCardHandler(MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter)
    : IRequestHandler<UpsertCardCommand, Outcome<Card>>
{
    public async Task<Outcome<Card>> Handle(UpsertCardCommand command, CancellationToken ct)
    {
        var scope = Scope.Parse(command.Scope);
        if (!await access.CanUpsertAsync(command.Caller, scope, ct))
            return Outcome<Card>.Forbidden($"No upsert grant on '{scope}'.");
        if (!limiter.TryAcquire(command.Caller.RateLimitKey))
            return Outcome<Card>.RateLimited("Upsert rate limit reached; try again later.");

        return command.Version == 0
            ? await CreateAsync(command, ct)
            : await UpdateAsync(command, ct);
    }

    private async Task<Outcome<Card>> CreateAsync(UpsertCardCommand command, CancellationToken ct)
    {
        var card = new Card
        {
            Scope = command.Scope,
            Key = command.Key,
            Version = 1,
            Type = Enum.Parse<CardType>(command.Type, ignoreCase: true),
            What = command.What,
            Why = command.Why,
            Who = command.Caller.Who,
            UpdatedAt = DateTime.UtcNow,
            Status = CardStatus.Published, // TODO: per-scope publish policy (D011).
            Body = command.Body,
            Source = command.Source
        };
        db.Cards.Add(card);

        try
        {
            await db.SaveChangesAsync(ct);
            return Outcome<Card>.Created(card);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await ConflictAsync(command, "Card already exists; read it and update its version.", ct);
        }
    }

    private async Task<Outcome<Card>> UpdateAsync(UpsertCardCommand command, CancellationToken ct)
    {
        var card = await FindAsync(command, ct);
        if (card is null)
            return Outcome<Card>.NotFound($"No card '{command.Scope}/{command.Key}'; create it with version 0.");
        if (card.Version != command.Version)
            return await ConflictAsync(command, $"Stale version {command.Version}.", ct);

        card.Type = Enum.Parse<CardType>(command.Type, ignoreCase: true);
        card.What = command.What;
        card.Why = command.Why;
        card.Who = command.Caller.Who;
        card.Body = command.Body ?? card.Body;
        card.Source = command.Source ?? card.Source;
        card.UpdatedAt = DateTime.UtcNow;
        card.Version++;

        try
        {
            await db.SaveChangesAsync(ct);
            return Outcome<Card>.Ok(card);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return await ConflictAsync(command, $"Stale version {command.Version}; another writer got there first.", ct);
        }
    }

    /// <summary>The current card is only returned to a caller who could read it anyway.</summary>
    private async Task<Outcome<Card>> ConflictAsync(UpsertCardCommand command, string message, CancellationToken ct)
    {
        var visible = await access.CanSeeAsync(command.Caller, Scope.Parse(command.Scope), ct);
        return Outcome<Card>.Conflict(visible ? await FindAsync(command, ct) : null, message);
    }

    private Task<Card?> FindAsync(UpsertCardCommand command, CancellationToken ct) =>
        db.Cards.SingleOrDefaultAsync(c => c.Scope == command.Scope && c.Key == command.Key, ct);
}
