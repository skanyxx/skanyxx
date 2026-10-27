using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Bodies;
using Skanyxx.Module.Memory.Coordination;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// The one write path for cards (memory-writes.md). An identical update is not a write: it returns the card as it is,
/// with no new version and no rate-limit slot, so a retrying agent cannot churn versions. A real write takes a slot per
/// caller and card, then per caller, then for everyone; keeps the version it replaces; and ends cached searches over the
/// scope. A scope holds at most its quota of cards (memory-store.md, <see cref="ScopeCapacity"/>).
/// </summary>
internal sealed class UpsertCardHandler(
    MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter, CardBodies bodies, SearchCache cache,
    ScopeCapacity capacity, IOptions<MemoryOptions> options)
    : IRequestHandler<UpsertCardCommand, Outcome<Card>>
{
    public async Task<Outcome<Card>> Handle(UpsertCardCommand command, CancellationToken ct)
    {
        var scope = Scope.Parse(command.Scope);
        if (!await access.CanUpsertAsync(command.Caller, scope, ct))
            return Outcome<Card>.Forbidden(AccessPolicy.UpsertRefusal(command.Caller, scope));

        try
        {
            var outcome = command.Version == 0
                ? await CreateAsync(command, scope, ct)
                : await UpdateAsync(command, ct);
            if (outcome.Status is OutcomeStatus.Ok or OutcomeStatus.Created)
                await cache.InvalidateAsync(command.Scope);
            return outcome;
        }
        catch (ObjectStoreUnavailableException)
        {
            return Outcome<Card>.Unavailable("The object store for card bodies did not answer; the card was not saved.");
        }
    }

    private async Task<Outcome<Card>> CreateAsync(UpsertCardCommand command, Scope scope, CancellationToken ct)
    {
        // Checked first so the usual conflict is not a failed INSERT, which EF logs at Error; the catch is for a race.
        // A create that meets an existing card stays a conflict (D041), even with the same content: version 0 says "new".
        if (await db.Cards.AnyAsync(c => c.Scope == command.Scope && c.Key == command.Key, ct))
            return await ConflictAsync(command, "Card already exists; read it and update its version.", ct);
        if (!await limiter.TryAcquireAsync(command.Caller.RateLimitKey, $"{command.Scope}/{command.Key}"))
            return Outcome<Card>.RateLimited("Upsert rate limit reached; try again later.");

        var (body, source) = await bodies.ResolveAsync(null, command.Body, command.Source, ct);
        // The quota check, any eviction and the insert commit together or not at all (C-2, C-4).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await capacity.MakeRoomAsync(scope, command.EvictExtracted, ct))
            return Outcome<Card>.Forbidden(ScopeCapacity.Full(scope));
        var now = DateTime.UtcNow;
        var card = new Card
        {
            Scope = command.Scope,
            Key = command.Key,
            Version = 1,
            Type = Enum.Parse<CardType>(command.Type, ignoreCase: true),
            What = command.What,
            Why = command.Why,
            Who = Who(command),
            UpdatedAt = now,
            CreatedAt = now,
            Status = StatusFor(command, CardStatus.Published),
            Body = body,
            Source = source,
            Tickets = [.. command.Tickets ?? []],
            Links = [.. command.Links ?? []],
            Keywords = [.. command.Keywords ?? []],
            Origin = command.Origin,
            AgentId = command.Caller.AgentId ?? command.ViaAgent
        };
        db.Cards.Add(card);

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Outcome<Card>.Created(card);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Rolled back first: the conflict's read cannot run in an aborted transaction, and any eviction is undone.
            await transaction.RollbackAsync(CancellationToken.None);
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
        if (SameContent(card, command))
            return Outcome<Card>.Ok(card);
        if (!await limiter.TryAcquireAsync(command.Caller.RateLimitKey, $"{command.Scope}/{command.Key}"))
            return Outcome<Card>.RateLimited("Upsert rate limit reached; try again later.");

        db.CardVersions.Add(CardVersion.Of(card));
        (card.Body, card.Source) = await bodies.ResolveAsync(card, command.Body, command.Source, ct);
        card.Type = Enum.Parse<CardType>(command.Type, ignoreCase: true);
        card.What = command.What;
        card.Why = command.Why;
        card.Who = Who(command);
        card.UpdatedAt = DateTime.UtcNow;
        card.Status = StatusFor(command, card.Status);
        card.Version++;
        if (command.Tickets is { } tickets)
            card.Tickets = [.. tickets];
        if (command.Links is { } links)
            card.Links = [.. links];
        if (command.Keywords is { } keywords)
            card.Keywords = [.. keywords];
        card.AgentId = command.Caller.AgentId ?? command.ViaAgent ?? card.AgentId;

        try
        {
            await db.SaveChangesAsync(ct);
            return Outcome<Card>.Ok(card);
        }
        catch (DbUpdateException ex) when (CardWrites.LostRace(ex))
        {
            db.ChangeTracker.Clear();
            return await ConflictAsync(command, $"Stale version {command.Version}; another writer got there first.", ct);
        }
    }

    /// <summary>
    /// D011: an agent's write into a review scope waits for a person; anything else keeps (or, on create, gets)
    /// <paramref name="otherwise"/>. A person's write to a candidate leaves it a candidate: publishing is its own act.
    /// </summary>
    private CardStatus StatusFor(UpsertCardCommand command, CardStatus otherwise) =>
        command.Caller.IsAgent && options.Value.ReviewScopes.Contains(command.Scope) ? CardStatus.Candidate : otherwise;

    private static string Who(UpsertCardCommand command) =>
        command.ViaAgent is { } agent && !command.Caller.IsAgent ? $"{command.Caller.Who} via {agent}" : command.Caller.Who;

    /// <summary>Nothing this write would change: a body or source sent along must be the stored one (pointers are never resent).</summary>
    private static bool SameContent(Card card, UpsertCardCommand command) =>
        string.Equals(card.Type.ToString(), command.Type, StringComparison.OrdinalIgnoreCase)
        && card.What == command.What && card.Why == command.Why
        && (command.Body is null || command.Body == card.Body) && (command.Source is null || command.Source == card.Source)
        && (command.Tickets is null || card.Tickets.SequenceEqual(command.Tickets))
        && (command.Links is null || card.Links.SequenceEqual(command.Links))
        && (command.Keywords is null || card.Keywords.SequenceEqual(command.Keywords));

    /// <summary>The current card is only returned to a caller who could read it anyway.</summary>
    private async Task<Outcome<Card>> ConflictAsync(UpsertCardCommand command, string message, CancellationToken ct)
    {
        var visible = await access.CanReadAsync(command.Caller, Scope.Parse(command.Scope), ct);
        return Outcome<Card>.Conflict(visible ? await FindAsync(command, ct) : null, message);
    }

    private Task<Card?> FindAsync(UpsertCardCommand command, CancellationToken ct) =>
        db.Cards.SingleOrDefaultAsync(c => c.Scope == command.Scope && c.Key == command.Key, ct);
}
