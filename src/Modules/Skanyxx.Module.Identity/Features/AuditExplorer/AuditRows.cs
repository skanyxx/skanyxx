using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>The rows an <see cref="AuditFilter"/> matches, as a query EF composes the sort, keyset and limit onto.</summary>
internal static class AuditRows
{
    public static IQueryable<AuditEntry> Matching(AccountsDbContext db, AuditFilter filter)
    {
        var where = new AuditWhere(filter);
        var sql = "SELECT * FROM identity_audit a WHERE " + where.Sql;
        return db.Audit.FromSqlRaw(sql, where.Parameters()).AsNoTracking();
    }

    public static IQueryable<AuditEntry> Sorted(IQueryable<AuditEntry> rows, bool ascending) =>
        ascending ? rows.OrderBy(a => a.AtUtc).ThenBy(a => a.Id) : rows.OrderByDescending(a => a.AtUtc).ThenByDescending(a => a.Id);
}
