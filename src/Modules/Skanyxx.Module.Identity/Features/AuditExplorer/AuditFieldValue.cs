namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>A value and how many hits hold it; <see cref="AuditFields.None"/> for an empty field.</summary>
internal sealed record AuditFieldValue(string Value, long Count);
