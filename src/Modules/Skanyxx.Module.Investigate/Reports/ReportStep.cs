namespace Skanyxx.Module.Investigate.Reports;

/// <summary>One step of a report, in tree order; <paramref name="Depth"/> 1 is a top-level step.</summary>
public sealed record ReportStep(
    string Id, string Title, string Kind, int Depth, string? Agent, string? Mode, string State,
    DateTime? StartedAt, DateTime? EndedAt, long? DurationMs, string? Output, string? Error);
