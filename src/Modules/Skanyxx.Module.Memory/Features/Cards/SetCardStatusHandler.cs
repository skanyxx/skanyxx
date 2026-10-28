using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Coordination;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// D011 and supersede (requirements.md §3.6), as a write to the card's scope: whoever may write there publishes a
/// candidate, marks a card stale, or brings it back. A replacement must be a card the person can open, so a stale
/// card never points at something its readers cannot see more of than its address. Stale cards leave search (agents
/// search published only) but stay in the library and keep their history.
/// </summary>
internal sealed class SetCardStatusHandler(
    MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter, SearchCache cache, ClientAddress client,
    ILogger<SetCardStatusHandler> logger)
    : IRequestHandler<SetCardStatusCommand, Outcome<CardDto>>
{
    public async Task<Outcome<CardDto>> Handle(SetCardStatusCommand command, CancellationToken ct) =>
        (await SetAsync(MemoryCaller.For(command.User), command, ct)).Map(CardMapper.ToDto);

    private async Task<Outcome<Card>> SetAsync(MemoryCaller caller, SetCardStatusCommand command, CancellationToken ct)
    {
        var scope = Scope.Parse(command.Scope);
        if (!await access.CanUpsertAsync(caller, scope, ct))
            return Outcome<Card>.Forbidden(AccessPolicy.UpsertRefusal(caller, scope));

        var card = await db.Cards.SingleOrDefaultAsync(c => c.Scope == command.Scope && c.Key == command.Key, ct);
        if (card is null)
            return Outcome<Card>.NotFound($"No card '{scope}/{command.Key}'.");
        if (card.Version != command.Version)
            return Outcome<Card>.Conflict(card, $"Stale version {command.Version}; the card is at version {card.Version}.");
        if (command.SupersededBy is { } replacement && !await OpensAsync(caller, replacement, card, ct))
            return Outcome<Card>.NotFound($"No card '{replacement}' to supersede it with.");

        var status = Enum.Parse<CardStatus>(command.Status, ignoreCase: true);
        if (card.Status == status && card.SupersededBy == command.SupersededBy)
            return Outcome<Card>.Ok(card);
        if (!await limiter.TryAcquireAsync(caller.RateLimitKey, $"{command.Scope}/{command.Key}"))
            return Outcome<Card>.RateLimited("Write rate limit reached; try again later.");

        db.CardVersions.Add(CardVersion.Of(card));
        var was = card.Status;
        card.Status = status;
        card.SupersededBy = command.SupersededBy;
        card.Version++;
        card.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CardWrites.LostRace(ex))
        {
            db.ChangeTracker.Clear();
            return Outcome<Card>.Conflict(
                await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == card.Id, ct),
                $"Stale version {command.Version}; another writer got there first.");
        }

        await cache.InvalidateAsync(command.Scope);
        logger.LogWarning("Card {Scope}/{Key} {From} -> {To} (version {Version}) by {ActorUserId} from {RemoteIp}",
            scope, card.Key, was, status, card.Version, caller.UserId, client.Current);
        return Outcome<Card>.Ok(card);
    }

    private async Task<bool> OpensAsync(MemoryCaller caller, string link, Card card, CancellationToken ct)
    {
        var slash = link.LastIndexOf('/');
        var (scope, key) = (link[..slash], link[(slash + 1)..]);
        if ((scope, key) == (card.Scope, card.Key) || !await access.CanReadAsync(caller, Scope.Parse(scope), ct))
            return false;
        var target = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Scope == scope && c.Key == key, ct);
        return target is not null && access.CanOpen(caller, target);
    }
}
