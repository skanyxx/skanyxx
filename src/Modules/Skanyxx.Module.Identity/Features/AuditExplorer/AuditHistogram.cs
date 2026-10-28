namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed record AuditHistogram(int IntervalSeconds, IReadOnlyList<AuditBucket> Buckets);
