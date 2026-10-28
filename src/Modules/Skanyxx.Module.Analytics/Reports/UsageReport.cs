using Skanyxx.Core.Platform.Analytics;
namespace Skanyxx.Module.Analytics.Reports;

/// <summary>
/// A scope's usage over a range: counts and tokens only, never what anyone asked or was told. <see cref="Previous"/> is
/// the same length right before (compare); <see cref="TopUsers"/> and <see cref="TopTeams"/> are empty for a person.
/// </summary>
public sealed record UsageReport(
    string ScopeKind, string? ScopeId, string ScopeName, DateOnly From, DateOnly To,
    UsageTotals Totals, UsageTotals? Previous, IReadOnlyList<string> Days,
    IReadOnlyList<long> TokensInPerDay, IReadOnlyList<long> TokensOutPerDay, IReadOnlyList<long> RequestsPerDay,
    IReadOnlyList<long> ErrorsPerDay, IReadOnlyList<long> SavedPerDay,
    IReadOnlyList<Series> TokensByModel, IReadOnlyList<Series> TokensByAgent, IReadOnlyList<Series> ChatsByAgent,
    IReadOnlyList<Ranked> TopUsers, IReadOnlyList<Ranked> TopTeams, IReadOnlyList<TableRow> Rows)
{
    public string CostDisclaimer => Core.Platform.Analytics.CostDisclaimer.Text;
}
