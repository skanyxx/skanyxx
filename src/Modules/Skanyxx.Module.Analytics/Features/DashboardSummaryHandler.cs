using System.Data.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Analytics.Data;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;
using Skanyxx.Module.Analytics.Usage;

namespace Skanyxx.Module.Analytics.Features;

/// <summary>
/// One read of the last 30 days of the rollup, cut into the Dashboard's numbers and charts: everyone for the owner, the
/// people in their own teams for a supervisor (the same reach Analytics gives them, <see cref="ScopeResolver"/>). A
/// supervisor's new memory cards are those in their teams' scopes, never a department's, the company's or anyone's personal ones.
/// </summary>
internal sealed class DashboardSummaryHandler(
    OrgReader orgs, UsageStore store, CostCalculator cost, IMediator mediator, TimeProvider clock, ILogger<DashboardSummaryHandler> logger)
    : IRequestHandler<DashboardSummaryQuery, DashboardSummary>
{
    public const int Days = 30;
    public const int RecentEventCount = 10;

    public async Task<DashboardSummary> Handle(DashboardSummaryQuery query, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var month = DateRange.Last(Days, today);
        var week = DateRange.Last(7, today);
        var org = await orgs.ReadAsync(ct);
        var owner = query.Viewer.IsOwner;
        var teams = owner ? null : org.TeamsOf(query.Viewer.UserId).ToList();
        var users = teams is null ? null : org.MembersOf(teams);
        var rows = await store.ReadAsync(month.From, month.To, users, ct);
        var weekRows = rows.Where(r => r.Day >= week.From).ToList();
        var todayRows = rows.Where(r => r.Day == today).ToList();
        var days = month.Each().ToList();
        var weekTotals = UsageTotals.Of(weekRows, cost);
        var todayTotals = UsageTotals.Of(todayRows, cost);

        return new DashboardSummary(
            org.People.Count(p => !p.Disabled && (users is null || users.Contains(p.Id))), todayTotals.ActiveUsers, weekTotals.ActiveUsers, todayTotals.Chats, weekTotals.Chats,
            todayTotals.TokensIn, todayTotals.TokensOut, todayTotals.ClientTokens, weekTotals.ClientTokens, UsageReportBuilder.PerDay(weekRows, [.. week.Each()], r => r.TokensIn + r.TokensOut),
            weekTotals.Requests, weekTotals.Unreported, weekTotals.CacheHitRate, weekTotals.TokensSaved,
            weekTotals.Cost, cost.Estimate(rows),
            Top(weekRows, r => r.Agent, k => k), owner ? Top(weekRows, r => r.UserId, org.PersonName) : null,
            [.. days.Select(d => d.ToString("yyyy-MM-dd"))],
            UsageReportBuilder.PerDay(rows, days, r => r.TokensIn), UsageReportBuilder.PerDay(rows, days, r => r.TokensOut),
            UsageReportBuilder.Stacked(rows, days, r => r.Agent, r => r.Chats), UsageReportBuilder.PerDay(rows, days, r => r.Errors),
            await CardsAsync(month, days, teams?.Select(t => $"team:{t.Slug}").ToList(), ct),
            owner ? await EventsAsync(org, ct) : null,
            Alerts(todayTotals, weekTotals));
    }

    private static IReadOnlyList<Ranked> Top(IEnumerable<UsageDay> rows, Func<UsageDay, string> key, Func<string, string> name) =>
        [.. rows.GroupBy(key)
            .Select(g => new Ranked(g.Key, name(g.Key), g.Sum(r => r.TokensIn + r.TokensOut), g.Sum(r => r.Requests), g.Sum(r => r.Chats)))
            .OrderByDescending(r => r.Requests).ThenByDescending(r => r.Tokens).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Take(5)];

    private async Task<IReadOnlyList<long>?> CardsAsync(DateRange month, IReadOnlyList<DateOnly> days, IReadOnlyCollection<string>? scopes, CancellationToken ct)
    {
        try
        {
            var counts = (await mediator.Send(new CardsPerDayQuery(month.From, month.To, scopes), ct)).ToDictionary(c => c.Day, c => c.Count);
            return [.. days.Select(d => counts.GetValueOrDefault(d))];
        }
        catch (Exception ex) when (ex is DbException or TimeoutException)
        {
            logger.LogWarning(ex, "Dashboard could not count new memory cards");
            return null;
        }
    }

    private async Task<IReadOnlyList<AdminEvent>> EventsAsync(OrgSnapshot org, CancellationToken ct)
    {
        var emails = org.People.ToDictionary(p => p.Id, p => p.Email);
        string? Name(string? id) => id is null ? null : emails.GetValueOrDefault(id, id);
        return [.. (await mediator.Send(new ListAuditQuery(Limit: RecentEventCount), ct)).Value!
            .Select(e => new AdminEvent(e.At, e.Action, Name(e.ActorId), Name(e.TargetId)))];
    }

    private static IReadOnlyList<DashboardAlert> Alerts(UsageTotals today, UsageTotals week)
    {
        List<DashboardAlert> alerts = [];
        if (today.Errors > 0)
            alerts.Add(new("warn", $"{today.Errors} chat turn(s) failed today (kagent or the agent did not answer).", "/Debug"));
        if (week.Unreported > 0)
            alerts.Add(new("info", $"kagent reported no token usage for {week.Unreported} of {week.Requests} turn(s) in the last 7 days; their tokens are not counted.", null));
        if (!week.Cost.Priced && week.Tokens > 0)
            alerts.Add(new("info", "No model prices are set (Analytics:Prices), so cost is not estimated.", null));
        return alerts;
    }
}
