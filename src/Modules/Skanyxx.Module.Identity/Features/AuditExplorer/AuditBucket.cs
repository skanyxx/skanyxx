namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>One histogram bar, stacked by outcome.</summary>
internal sealed record AuditBucket(DateTimeOffset At, long Success, long Refused, long Failed);
