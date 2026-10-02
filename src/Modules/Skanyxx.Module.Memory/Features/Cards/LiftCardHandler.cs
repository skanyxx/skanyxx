using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class LiftCardHandler(
    MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter, ClientAddress client, ILogger<LiftCardHandler> logger)
    : IRequestHandler<LiftCardCommand, Outcome<Card>>
{
    public async Task<Outcome<Card>> Handle(LiftCardCommand command, CancellationToken ct)
    {
        var from = Scope.Parse(command.FromScope);
        var to = Scope.Parse(command.ToScope);
        if (!await access.CanReadAsync(command.Caller, from, ct))
            return Outcome<Card>.Forbidden($"'{from}' is not readable by this caller.");
        if (!await access.CanUpsertAsync(command.Caller, to, ct))
            return Outcome<Card>.Forbidden($"Lifting into '{to}' needs write rights there (team or department: a member; company: a supervisor).");
        // Decided before the write and the rate-limit slot, so a failing membership lookup neither turns a committed lift
        // into a 500 nor spends a write slot.
        var oversight = await IsOversightAsync(command.Caller, from, ct);
        if (!limiter.TryAcquire(command.Caller.RateLimitKey))
            return Outcome<Card>.RateLimited("Write rate limit reached; try again later.");

        var source = await db.Cards.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Scope == command.FromScope && c.Key == command.Key, ct);
        if (source is null)
            return Outcome<Card>.NotFound($"No card '{from}/{command.Key}'.");
        if (source.Status != CardStatus.Published)
            return Outcome<Card>.Conflict(null, "Only published cards can be lifted.");

        var copy = new Card
        {
            Scope = to.ToString(),
            Key = source.Key,
            Version = 1,
            Type = source.Type,
            What = source.What,
            Why = source.Why,
            Who = command.Caller.Who,
            UpdatedAt = DateTime.UtcNow,
            Status = CardStatus.Published,
            Body = source.Body,
            Source = source.Source,
            LiftedFromId = source.Id
        };

        // Checked first so the usual conflict is not a failed INSERT, which EF logs at Error; the catch is for a race.
        if (await db.Cards.AnyAsync(c => c.Scope == copy.Scope && c.Key == copy.Key, ct))
            return await ExistsAsync(command, to, copy.Key, ct);
        db.Cards.Add(copy);

        try
        {
            await db.SaveChangesAsync(ct);
            if (oversight)
                logger.LogWarning("Card {Key} lifted from {SourceScope} to {TargetScope} by {ActorUserId} from {RemoteIp}, who is not a member of the source",
                    command.Key, from, to, command.Caller.UserId, client.Current);
            return Outcome<Card>.Created(copy);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await ExistsAsync(command, to, copy.Key, ct);
        }
    }

    private async Task<Outcome<Card>> ExistsAsync(LiftCardCommand command, Scope to, string key, CancellationToken ct)
    {
        var scope = to.ToString();
        var existing = await access.CanReadAsync(command.Caller, to, ct)
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
