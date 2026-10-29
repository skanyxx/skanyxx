using Microsoft.EntityFrameworkCore;

namespace Skanyxx.Module.Investigate.Data;

/// <summary>
/// The database's clock, for times written as values rather than in an update (D373): replicas' clocks may disagree,
/// and a run's times are compared with each other and with the stale sweep's.
/// </summary>
internal static class DbClock
{
    public static Task<DateTime> NowAsync(InvestigateDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTime>($"SELECT now() AS \"Value\"").SingleAsync(ct);
}
