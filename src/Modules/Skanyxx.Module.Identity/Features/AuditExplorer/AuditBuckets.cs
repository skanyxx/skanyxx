using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// Counts per time bucket and outcome, one GROUP BY on <c>date_bin</c>. <paramref name="extra"/> narrows the filter with
/// fixed SQL; <paramref name="weight"/> is what each row counts for (1, or a sampled refusal's share).
/// </summary>
internal static class AuditBuckets
{
    public static async Task<(TimeSpan Interval, List<BucketRow> Rows)> CountAsync(
        AccountsDbContext db, AuditFilter filter, CancellationToken ct, string extra = "", string weight = "1")
    {
        var interval = AuditInterval.For(filter.From, filter.To);
        var where = new AuditWhere(filter);
        var sql = $"""
            SELECT date_bin({where.Parameter(interval)}, a."AtUtc", {where.Parameter(AuditInterval.Origin)}) AS "At", a."Outcome" AS "Outcome",
                   sum({weight})::bigint AS "Count"
            FROM identity_audit a WHERE {where.Sql}{extra}
            GROUP BY 1, 2
            """;
        return (interval, await db.Database.SqlQueryRaw<BucketRow>(sql, where.Parameters()).ToListAsync(ct));
    }
}
