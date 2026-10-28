namespace Skanyxx.Module.Identity.Features.AuditExplorer;

internal sealed record AuditSignInBucket(DateTimeOffset At, long Succeeded, long Failed);
