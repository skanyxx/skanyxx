using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed class AuditHistogramHandler(AccountsDbContext db) : IRequestHandler<AuditHistogramQuery, Outcome<AuditHistogram>>
{
    public async Task<Outcome<AuditHistogram>> Handle(AuditHistogramQuery query, CancellationToken ct)
    {
        var (interval, rows) = await AuditBuckets.CountAsync(db, query.Filter, ct);
        long Count(DateTimeOffset at, string outcome) => rows.Where(r => r.At == at && r.Outcome == outcome).Sum(r => r.Count);
        var buckets = AuditInterval.Starts(query.Filter.From, query.Filter.To, interval)
            .Select(at => new AuditBucket(at, Count(at, AuditColumns.Success), Count(at, AuditColumns.Refused), Count(at, AuditColumns.Failed)));
        return Outcome<AuditHistogram>.Ok(new AuditHistogram((int)interval.TotalSeconds, [.. buckets]));
    }
}
