using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// The versions a card replaced, newest first: for whoever may open the card now (the scope, then D103), exactly like
/// the card itself. Versions under an older key (before a rename) are the same card's and listed too.
/// </summary>
internal sealed class CardHistoryHandler(MemoryDbContext db, AccessPolicy access)
    : IRequestHandler<CardHistoryQuery, Outcome<IReadOnlyList<CardVersionDto>>>
{
    public const int Limit = 100;

    public async Task<Outcome<IReadOnlyList<CardVersionDto>>> Handle(CardHistoryQuery query, CancellationToken ct)
    {
        var caller = MemoryCaller.For(query.User);
        var scope = Scope.Parse(query.Scope);
        if (!await access.CanReadAsync(caller, scope, ct))
            return Outcome<IReadOnlyList<CardVersionDto>>.Forbidden($"'{scope}' is not readable by this caller.");

        var card = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Scope == query.Scope && c.Key == query.Key, ct);
        if (card is null || !access.CanOpen(caller, card))
            return Outcome<IReadOnlyList<CardVersionDto>>.NotFound($"No card '{scope}/{query.Key}'.");

        var versions = await db.CardVersions.AsNoTracking()
            .Where(v => v.CardId == card.Id)
            .OrderByDescending(v => v.Version)
            .Take(Limit)
            .ToListAsync(ct);
        return Outcome<IReadOnlyList<CardVersionDto>>.Ok(versions.Select(v => v.ToDto()).ToList());
    }
}
