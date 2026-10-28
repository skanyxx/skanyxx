namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// One explorer query: the time range [<paramref name="From"/>, <paramref name="To"/>), free text matched anywhere in a
/// row (and against the actor's and target's emails), and field chips, all ANDed as in Kibana.
/// </summary>
internal sealed record AuditFilter(DateTimeOffset From, DateTimeOffset To, string? Q, IReadOnlyList<AuditFieldFilter> Fields);
