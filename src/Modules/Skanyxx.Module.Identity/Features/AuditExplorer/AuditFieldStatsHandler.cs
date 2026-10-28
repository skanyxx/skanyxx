using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>One count and one GROUP BY per field, each bounded by the filter's time range and <see cref="AuditFieldStatsQuery.Top"/>.</summary>
internal sealed class AuditFieldStatsHandler(AccountsDbContext db) : IRequestHandler<AuditFieldStatsQuery, Outcome<IReadOnlyList<AuditFieldStats>>>
{
    public async Task<Outcome<IReadOnlyList<AuditFieldStats>>> Handle(AuditFieldStatsQuery query, CancellationToken ct)
    {
        var where = new AuditWhere(query.Filter);
        var totalSql = """SELECT count(*) AS "Value" FROM identity_audit a WHERE """ + where.Sql;
        var total = await db.Database.SqlQueryRaw<long>(totalSql, where.Parameters()).SingleAsync(ct);
        var stats = new List<AuditFieldStats>();
        foreach (var field in AuditFields.Names)
        {
            var (value, from) = field == AuditFields.Role
                ? ("r.\"RoleId\"", "identity_audit a LEFT JOIN identity_user_roles r ON r.\"UserId\" = a.\"ActorId\"")
                : (AuditFields.Column(field), "identity_audit a");
            var sql = $"""
                SELECT {value} AS "Value", count(*) AS "Count" FROM {from} WHERE {where.Sql}
                GROUP BY 1 ORDER BY 2 DESC, 1 LIMIT {AuditFieldStatsQuery.Top}
                """;
            var rows = await db.Database.SqlQueryRaw<FieldCountRow>(sql, where.Parameters()).ToListAsync(ct);
            stats.Add(new AuditFieldStats(field, total, [.. rows.Select(r => new AuditFieldValue(r.Value ?? AuditFields.None, r.Count))]));
        }
        return Outcome<IReadOnlyList<AuditFieldStats>>.Ok(stats);
    }
}
