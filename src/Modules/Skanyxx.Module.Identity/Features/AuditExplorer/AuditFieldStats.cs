namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>A field's top values among the query's <paramref name="Total"/> hits (a person with two roles counts under both).</summary>
internal sealed record AuditFieldStats(string Field, long Total, IReadOnlyList<AuditFieldValue> Values);
