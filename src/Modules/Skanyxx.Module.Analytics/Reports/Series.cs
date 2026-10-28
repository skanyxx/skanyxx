namespace Skanyxx.Module.Analytics.Reports;

/// <summary>One line or stack of a chart: a value per day of the report's range.</summary>
public sealed record Series(string Name, IReadOnlyList<long> Values);
