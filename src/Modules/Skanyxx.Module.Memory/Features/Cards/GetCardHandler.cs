using Skanyxx.Core.Platform;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class GetCardHandler(MemoryDbContext db, AccessPolicy access) : IRequestHandler<GetCardQuery, Outcome<Card>>
{
    public async Task<Outcome<Card>> Handle(GetCardQuery query, CancellationToken ct)
    {
        if (!access.CanRead(query.Caller, Scope.Parse(query.Scope)))
            return Outcome<Card>.Forbidden($"'{query.Scope}' is not readable by this caller.");

        var card = await db.Cards.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Scope == query.Scope && c.Key == query.Key, ct);
        return card is null ? Outcome<Card>.NotFound($"No card '{query.Scope}/{query.Key}'.") : Outcome<Card>.Ok(card);
    }
}
