namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>Where the next page starts: after this row in the page's sort order (time, then id).</summary>
internal sealed record AuditCursor(DateTimeOffset At, long Id);
