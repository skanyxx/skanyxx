using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Module.Analytics.Reports;

namespace Skanyxx.Module.Analytics.Features;

/// <summary>
/// The Dashboard's usage at a glance and its compact charts (the last 30 UTC days, today last): the whole install for the
/// owner, a supervisor's own teams for a supervisor. Per-person lists
/// (<see cref="TopUsers"/>) and the audit (<see cref="RecentEvents"/>) are the owner's: null for a supervisor.
/// <see cref="CardsPerDay"/> is null when memory could not be read.
/// </summary>
public sealed record DashboardSummary(
    int People, int ActiveToday, int Active7d, long ChatsToday, long Chats7d,
    long TokensInToday, long TokensOutToday, IReadOnlyList<long> Tokens7d, long Requests7d, long Unreported7d,
    double CacheHitRate7d, long TokensSaved7d, CostEstimate Cost7d, CostEstimate Cost30d,
    IReadOnlyList<Ranked> TopAgents, IReadOnlyList<Ranked>? TopUsers,
    IReadOnlyList<string> Days, IReadOnlyList<long> TokensInPerDay, IReadOnlyList<long> TokensOutPerDay,
    IReadOnlyList<Series> ChatsByAgent, IReadOnlyList<long> ErrorsPerDay, IReadOnlyList<long>? CardsPerDay,
    IReadOnlyList<AdminEvent>? RecentEvents, IReadOnlyList<DashboardAlert> Alerts)
{
    public string CostDisclaimer => Core.Platform.Analytics.CostDisclaimer.Text;
}
