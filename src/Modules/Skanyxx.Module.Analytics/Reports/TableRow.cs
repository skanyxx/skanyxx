namespace Skanyxx.Module.Analytics.Reports;

/// <summary>
/// One row of the report's table. <paramref name="Kind"/> and <paramref name="Id"/> name the scope a click opens; Kind
/// null = not a scope (an agent under a person, or "not in a team").
/// </summary>
public sealed record TableRow(string? Kind, string Id, string Name, UsageTotals Totals);
