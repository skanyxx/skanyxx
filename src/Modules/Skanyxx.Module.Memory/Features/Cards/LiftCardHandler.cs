using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Bodies;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Coordination;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// Copies a published card into a wider scope. The copy is a new write into a shared scope, so it is held to D391 like
/// one: a card written before the credential check (or a body in the object store) that holds a credential or hidden
/// text is refused here rather than published further.
/// </summary>
internal sealed class LiftCardHandler(
    MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter, SearchCache cache, ScopeCapacity capacity,
    CardBodies bodies, ClientAddress client, ILogger<LiftCardHandler> logger)
    : IRequestHandler<LiftCardCommand, Outcome<CardDto>>
{
    public async Task<Outcome<CardDto>> Handle(LiftCardCommand command, CancellationToken ct) =>
        (await LiftAsync(MemoryCaller.For(command.User), command, ct)).Map(CardMapper.ToDto);

    private async Task<Outcome<Card>> LiftAsync(MemoryCaller caller, LiftCardCommand command, CancellationToken ct)
    {
        var from = Scope.Parse(command.FromScope);
        var to = Scope.Parse(command.ToScope);
        if (!await access.CanReadAsync(caller, from, ct))
            return Outcome<Card>.Forbidden($"'{from}' is not readable by this caller.");
        if (!await access.CanUpsertAsync(caller, to, ct))
            return Outcome<Card>.Forbidden($"Lifting into '{to}' needs write rights there (team or department: a member; company: a supervisor).");
        // Decided before the write and the rate-limit slot, so a failing membership lookup neither turns a committed lift
        // into a 500 nor spends a write slot.
        var oversight = await IsOversightAsync(caller, from, ct);
        if (!await limiter.TryAcquireAsync(caller.RateLimitKey))
            return Outcome<Card>.RateLimited("Write rate limit reached; try again later.");

        var source = await db.Cards.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Scope == command.FromScope && c.Key == command.Key, ct);
        if (source is null)
            return Outcome<Card>.NotFound($"No card '{from}/{command.Key}'.");
        if (source.Status != CardStatus.Published)
            return Outcome<Card>.Conflict(null, "Only published cards can be lifted.");
        if (BodyPointer.IsPointer(source.Source) && !bodies.Enabled)
            return Outcome<Card>.Unavailable("This card's body is in an object store that is not configured (Memory:ObjectStore), so it could not be checked for credentials.");
        string? body;
        try
        {
            body = await BodyTextAsync(source, ct);
        }
        catch (ObjectStoreUnavailableException)
        {
            return Outcome<Card>.Unavailable("The object store for card bodies did not answer, so the body could not be checked for credentials.");
        }
        var texts = new[] { source.What, source.Why, source.Source, body }.OfType<string>().ToList();
        if (texts.Any(SecretScan.HasHiddenText))
            return Outcome<Card>.Conflict(null, "The card holds Unicode tag characters (U+E0000–U+E007F) or stray variation selectors, hidden text a model would read. Edit it first.");
        if (texts.Select(SecretScan.Find).OfType<string>().FirstOrDefault() is { } secret)
        {
            logger.LogWarning("Lift of {Scope}/{Key} by {ActorUserId} refused: the card seems to hold {Secret}", command.FromScope, command.Key, caller.UserId, secret);
            return Outcome<Card>.Conflict(null, $"The card seems to hold {secret}: memory never publishes credentials further. Edit it first.");
        }

        var copy = new Card
        {
            Scope = to.ToString(),
            Key = source.Key,
            Version = 1,
            Type = source.Type,
            What = source.What,
            Why = source.Why,
            Who = caller.Who,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = CardStatus.Published,
            Body = source.Body,
            Source = source.Source,
            LiftedFromId = source.Id,
            Tickets = source.Tickets,
            Keywords = source.Keywords,
            // A link into a lower scope would name a card the new readers cannot open: it stays on the original only.
            Links = [.. source.Links.Where(l => CardLinks.CardScope(l) is not { } linked || linked.Level >= to.Level)],
            Origin = source.Origin,
            AgentId = source.AgentId
        };

        // Checked first so the usual conflict is not a failed INSERT, which EF logs at Error; the catch is for a race.
        if (await db.Cards.AnyAsync(c => c.Scope == copy.Scope && c.Key == copy.Key, ct))
            return await ExistsAsync(caller, to, copy.Key, ct);
        // A lift adds a card like a create does, so it counts against the target's quota the same way (C-4, D307).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await capacity.MakeRoomAsync(to, evictExtracted: false, ct))
            return Outcome<Card>.Forbidden(ScopeCapacity.Full(to));
        db.Cards.Add(copy);

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await cache.InvalidateAsync(copy.Scope);
            if (oversight)
                logger.LogWarning("Card {Key} lifted from {SourceScope} to {TargetScope} by {ActorUserId} from {RemoteIp}, who is not a member of the source",
                    command.Key, from, to, caller.UserId, client.Current);
            return Outcome<Card>.Created(copy);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return await ExistsAsync(caller, to, copy.Key, ct);
        }
    }

    /// <summary>The body as text, from Postgres or the object store; null when it has none.</summary>
    private async Task<string?> BodyTextAsync(Card card, CancellationToken ct)
    {
        if (!BodyPointer.IsPointer(card.Source))
            return card.Body;
        if (await bodies.OpenAsync(card.Source, ct) is not { } stored)
            return null;
        await using var content = stored.Content;
        using var reader = new StreamReader(content);
        return await reader.ReadToEndAsync(ct);
    }

    private async Task<Outcome<Card>> ExistsAsync(MemoryCaller caller, Scope to, string key, CancellationToken ct)
    {
        var scope = to.ToString();
        var existing = await access.CanReadAsync(caller, to, ct)
            ? await db.Cards.AsNoTracking().SingleAsync(c => c.Scope == scope && c.Key == key, ct)
            : null;
        return Outcome<Card>.Conflict(existing, $"'{to}/{key}' already exists; update it instead.");
    }

    /// <summary>
    /// A supervisor's oversight lets them publish a team's card beyond the team without anyone in it taking part (D091):
    /// allowed, since supervisors gate company, but always on the record. Anyone else who could read a team or
    /// department scope is a member of it, so only a supervisor is asked.
    /// </summary>
    private async Task<bool> IsOversightAsync(MemoryCaller caller, Scope from, CancellationToken ct) =>
        access.IsSupervisor(caller) && from.Level is ScopeLevel.Team or ScopeLevel.Department && !await access.IsMemberAsync(caller, from, ct);
}
