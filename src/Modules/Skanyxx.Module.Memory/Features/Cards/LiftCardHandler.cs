using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class LiftCardHandler(MemoryDbContext db, AccessPolicy access, UpsertRateLimiter limiter) : IRequestHandler<LiftCardCommand, Outcome<Card>>
{
    public async Task<Outcome<Card>> Handle(LiftCardCommand command, CancellationToken ct)
    {
        var from = Scope.Parse(command.FromScope);
        var to = Scope.Parse(command.ToScope);
        if (!access.CanRead(command.Caller, from))
            return Outcome<Card>.Forbidden($"'{from}' is not readable by this caller.");
        if (!await access.CanUpsertAsync(command.Caller, to, ct))
            return Outcome<Card>.Forbidden($"Lifting into '{to}' needs write rights there (company: a supervisor).");
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
        db.Cards.Add(copy);

        try
        {
            await db.SaveChangesAsync(ct);
            return Outcome<Card>.Created(copy);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var existing = await access.CanSeeAsync(command.Caller, to, ct)
                ? await db.Cards.AsNoTracking().SingleAsync(c => c.Scope == copy.Scope && c.Key == copy.Key, ct)
                : null;
            return Outcome<Card>.Conflict(existing, $"'{to}/{copy.Key}' already exists; update it instead.");
        }
    }
}
