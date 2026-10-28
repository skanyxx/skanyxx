using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class SearchAuditHandler(AccountsDbContext db) : IRequestHandler<SearchAuditQuery, Outcome<AuditPage>>
{
    public async Task<Outcome<AuditPage>> Handle(SearchAuditQuery query, CancellationToken ct)
    {
        var rows = AuditRows.Matching(db, query.Filter);
        var total = await rows.LongCountAsync(ct);
        if (query.After is { } after)
            rows = query.Ascending
                ? rows.Where(a => a.AtUtc > after.At || (a.AtUtc == after.At && a.Id > after.Id))
                : rows.Where(a => a.AtUtc < after.At || (a.AtUtc == after.At && a.Id < after.Id));
        var page = await AuditRows.Sorted(rows, query.Ascending).Take(query.Limit + 1).ToListAsync(ct);
        var shown = page.Take(query.Limit).ToList();
        var next = page.Count > query.Limit ? new AuditCursor(shown[^1].AtUtc, shown[^1].Id) : null;
        return Outcome<AuditPage>.Ok(new AuditPage(total, [.. shown.Select(AuditEvent.From)], next));
    }
}
