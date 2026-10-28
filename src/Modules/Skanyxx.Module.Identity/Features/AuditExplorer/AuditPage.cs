namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>One page of hits; <paramref name="Total"/> counts every hit of the query, <paramref name="Next"/> is null on the last page.</summary>
internal sealed record AuditPage(long Total, IReadOnlyList<AuditEvent> Entries, AuditCursor? Next);
