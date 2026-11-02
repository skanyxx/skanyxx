namespace Skanyxx.Module.Investigate.Reports;

/// <summary>
/// One step of a report, in tree order; <paramref name="Depth"/> 1 is a top-level step. <paramref name="KagentReason"/>:
/// kagent's own words for a failed step, null for anyone but builders and up (D556).
/// </summary>
public sealed record ReportStep(
    string Id, string Title, string Kind, int Depth, string? Agent, string? Mode, string State,
    DateTime? StartedAt, DateTime? EndedAt, long? DurationMs, string? Output, string? Error, string? KagentReason);
