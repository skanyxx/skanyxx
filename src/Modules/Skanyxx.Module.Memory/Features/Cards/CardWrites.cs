using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Skanyxx.Module.Memory.Features.Cards;

internal static class CardWrites
{
    private const string VersionIndex = "IX_memory_card_versions_card_id_version";

    /// <summary>
    /// Another writer committed the same version first (D041). The version history's unique index can notice before the
    /// card's concurrency token does — both writers insert the version they replace — so both mean "stale".
    /// </summary>
    public static bool LostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException
        || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: VersionIndex };
}
