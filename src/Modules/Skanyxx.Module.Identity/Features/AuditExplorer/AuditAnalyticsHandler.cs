using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// Sign-ins are <c>signin.succeeded</c> (password or Microsoft), <c>signin.refused</c> (every refused password, which
/// covers the managed-account and bootstrap-token rows) and refused Microsoft sign-ins. A sampled refusal (D167) counts for itself plus
/// the <c>skippedBefore</c> it stands for, so the failed line shows the volume, not the sampling.
/// </summary>
internal sealed class AuditAnalyticsHandler(AccountsDbContext db) : IRequestHandler<AuditAnalyticsQuery, Outcome<AuditAnalytics>>
{
    private const string SignIns = """ AND a."Action" IN ('signin.succeeded', 'signin.refused', 'entra.signin_refused')""";
    private const string Weight = """1 + COALESCE((a."Details"->>'skippedBefore')::int, 0)""";

    public async Task<Outcome<AuditAnalytics>> Handle(AuditAnalyticsQuery query, CancellationToken ct)
    {
        var (interval, rows) = await AuditBuckets.CountAsync(db, query.Filter, ct, SignIns, Weight);
        var signIns = AuditInterval.Starts(query.Filter.From, query.Filter.To, interval).Select(at => new AuditSignInBucket(at,
            rows.Where(r => r.At == at && r.Outcome == AuditColumns.Success).Sum(r => r.Count),
            rows.Where(r => r.At == at && r.Outcome != AuditColumns.Success).Sum(r => r.Count)));

        var where = new AuditWhere(query.Filter);
        var sql = """SELECT a."Action" AS "Value", count(*) AS "Count" FROM identity_audit a WHERE """ + where.Sql
            + """ AND a."Outcome" = 'success' GROUP BY 1""";
        var actions = await db.Database.SqlQueryRaw<FieldCountRow>(sql, where.Parameters()).ToListAsync(ct);
        var changes = AdminChangeCategories.All.Select(category => new AuditFieldValue(category,
            actions.Where(a => AdminChangeCategories.Of(a.Value!) == category).Sum(a => a.Count)));

        return Outcome<AuditAnalytics>.Ok(new AuditAnalytics((int)interval.TotalSeconds, [.. signIns], [.. changes]));
    }
}
