namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>The dashboard cards the histogram and field stats do not already give: sign-ins over time and admin changes by kind.</summary>
internal sealed record AuditAnalytics(int IntervalSeconds, IReadOnlyList<AuditSignInBucket> SignIns, IReadOnlyList<AuditFieldValue> AdminChanges);
