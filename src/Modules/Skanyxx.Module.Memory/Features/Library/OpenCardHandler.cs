using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Access;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Library;

/// <summary>
/// One card as the library opens it: the same read rules as <c>GET api/memory/cards/{scope}/{key}</c> (the scope, then
/// D103 for company drafts), plus what this person may do to it, all from <see cref="AccessPolicy"/> (D2: lift only into
/// a higher scope they may write).
/// </summary>
internal sealed class OpenCardHandler(MemoryDbContext db, AccessPolicy access) : IRequestHandler<OpenCardQuery, Outcome<LibraryCard>>
{
    public async Task<Outcome<LibraryCard>> Handle(OpenCardQuery query, CancellationToken ct)
    {
        var caller = MemoryCaller.For(query.User);
        var scope = Scope.Parse(query.Scope);
        if (!await access.CanReadAsync(caller, scope, ct))
            return Outcome<LibraryCard>.Forbidden($"'{scope}' is not readable by this caller.");

        var card = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Scope == query.Scope && c.Key == query.Key, ct);
        if (card is null || !access.CanOpen(caller, card))
            return Outcome<LibraryCard>.NotFound($"No card '{scope}/{query.Key}'.");

        var liftTargets = card.Status == CardStatus.Published
            ? (await access.WritableScopesAsync(caller, ct)).Where(s => s.Level > scope.Level).Select(s => s.ToString()).ToList()
            : [];
        return Outcome<LibraryCard>.Ok(new LibraryCard(
            card.ToDto(), await LiftedFromAsync(caller, card, ct), liftTargets, await access.CanUpsertAsync(caller, scope, ct)));
    }

    /// <summary>Named only to someone who could open it: a lifted copy must not reveal a personal card's key.</summary>
    private async Task<string?> LiftedFromAsync(MemoryCaller caller, Card card, CancellationToken ct)
    {
        if (card.LiftedFromId is not { } id)
            return null;
        var source = await db.Cards.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Scope, c.Key }).SingleOrDefaultAsync(ct);
        return source is not null && await access.CanReadAsync(caller, Scope.Parse(source.Scope), ct) ? $"{source.Scope}/{source.Key}" : null;
    }
}
