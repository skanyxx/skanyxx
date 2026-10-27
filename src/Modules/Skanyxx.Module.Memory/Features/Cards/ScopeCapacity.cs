using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Memory.Data;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>
/// D307's quotas, for every path that adds a card to a scope (create, lift, extraction). Called inside the writer's
/// transaction: it takes the scope's lock (held until commit or rollback, so two writers cannot both see room for one
/// card), then says whether one more card fits — dropping the scope's oldest extracted card first when the writer may.
/// The drop is part of the writer's transaction, so a write that fails afterwards takes nothing with it.
/// </summary>
internal sealed class ScopeCapacity(MemoryDbContext db, IOptions<MemoryOptions> options)
{
    /// <summary>Two-int advisory lock class <c>(0x4D454D04, hashtext(scope))</c>; see <see cref="MemoryMigrator"/>.</summary>
    internal const int LockClass = 0x4D454D04;

    public async Task<bool> MakeRoomAsync(Scope scope, bool evictExtracted, CancellationToken ct)
    {
        var name = scope.ToString();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockClass}, hashtext({name}))", ct);
        var quota = scope.Level == ScopeLevel.Personal ? options.Value.MaxCardsPerPersonalScope : options.Value.MaxCardsPerScope;
        if (await db.Cards.CountAsync(c => c.Scope == name, ct) < quota)
            return true;
        if (!evictExtracted)
            return false;
        return await db.Cards
            .Where(c => c.Scope == name && c.Origin != CardOrigin.Manual)
            .OrderBy(c => c.UpdatedAt)
            .Take(1)
            .ExecuteDeleteAsync(ct) > 0;
    }

    public static string Full(Scope scope) =>
        $"'{scope}' is full; mark cards stale or ask a supervisor to raise Memory:MaxCards*.";
}
