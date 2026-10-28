using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Analytics.Data;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Reports;

/// <summary>Turns rollup rows already narrowed to a scope's people into its charts, top lists and table.</summary>
public sealed class UsageReportBuilder(CostCalculator cost)
{
    /// <summary>A chart keeps this many series and folds the rest into "Other" (the theme has eight chart colours).</summary>
    public const int MaxSeries = 7;

    public const int TopCount = 10;

    public UsageReport Build(ResolvedScope scope, DateRange range, IReadOnlyCollection<UsageDay> rows,
        IReadOnlyCollection<UsageDay>? previous, OrgSnapshot org)
    {
        var days = range.Each().ToList();
        var person = scope.Scope.Kind == ScopeKind.User;
        return new UsageReport(
            scope.Scope.Kind.ToString().ToLowerInvariant(), scope.Scope.Id, scope.Name, range.From, range.To,
            UsageTotals.Of(rows, cost), previous is null ? null : UsageTotals.Of(previous, cost),
            [.. days.Select(d => d.ToString("yyyy-MM-dd"))],
            PerDay(rows, days, r => r.TokensIn), PerDay(rows, days, r => r.TokensOut), PerDay(rows, days, r => r.Requests),
            PerDay(rows, days, r => r.Errors), PerDay(rows, days, r => r.TokensSaved),
            Stacked(rows, days, r => r.Model, r => r.TokensIn + r.TokensOut),
            Stacked(rows, days, r => r.Agent, r => r.TokensIn + r.TokensOut),
            Stacked(rows, days, r => r.Agent, r => r.Chats),
            person ? [] : TopUsers(rows, org),
            scope.Scope.Kind switch
            {
                ScopeKind.Org => TopTeams(rows, org.Teams),
                ScopeKind.Department => TopTeams(rows, org.Teams.Where(t => t.Department == scope.Scope.Id)),
                _ => []
            },
            person ? AgentRows(rows) : GroupRows(rows, scope.Children));
    }

    public static IReadOnlyList<long> PerDay(IEnumerable<UsageDay> rows, IReadOnlyList<DateOnly> days, Func<UsageDay, long> value)
    {
        var sums = rows.GroupBy(r => r.Day).ToDictionary(g => g.Key, g => g.Sum(value));
        return [.. days.Select(d => sums.GetValueOrDefault(d))];
    }

    /// <summary>The <see cref="MaxSeries"/> biggest keys by total, then "Other"; keys that sum to nothing are left out.</summary>
    public static IReadOnlyList<Series> Stacked(IReadOnlyCollection<UsageDay> rows, IReadOnlyList<DateOnly> days,
        Func<UsageDay, string> key, Func<UsageDay, long> value)
    {
        var ranked = rows.GroupBy(key).Select(g => (g.Key, Total: g.Sum(value))).Where(g => g.Total > 0)
            .OrderByDescending(g => g.Total).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).ToList();
        var kept = ranked.Take(MaxSeries).ToHashSet();
        List<Series> series = [.. kept.Select(k => new Series(k, PerDay(rows.Where(r => key(r) == k), days, value)))];
        if (ranked.Count > MaxSeries)
            series.Add(new Series("Other", PerDay(rows.Where(r => !kept.Contains(key(r))), days, value)));
        return series;
    }

    private static IReadOnlyList<Ranked> TopUsers(IReadOnlyCollection<UsageDay> rows, OrgSnapshot org) =>
        [.. rows.GroupBy(r => r.UserId)
            .Select(g => new Ranked(g.Key, org.PersonName(g.Key), g.Sum(r => r.TokensIn + r.TokensOut), g.Sum(r => r.Requests), g.Sum(r => r.Chats)))
            .OrderByDescending(r => r.Tokens).ThenByDescending(r => r.Requests).ThenBy(r => r.Name, StringComparer.Ordinal).Take(TopCount)];

    private static IReadOnlyList<Ranked> TopTeams(IReadOnlyCollection<UsageDay> rows, IEnumerable<TeamDto> teams) =>
        [.. teams
            .Select(t => (Team: t, Rows: rows.Where(r => t.Members.Contains(r.UserId)).ToList()))
            .Select(t => new Ranked(t.Team.Slug, t.Team.Name, t.Rows.Sum(r => r.TokensIn + r.TokensOut), t.Rows.Sum(r => r.Requests), t.Rows.Sum(r => r.Chats)))
            .Where(r => r.Requests > 0)
            .OrderByDescending(r => r.Tokens).ThenBy(r => r.Name, StringComparer.Ordinal).Take(TopCount)];

    private IReadOnlyList<TableRow> GroupRows(IReadOnlyCollection<UsageDay> rows, IReadOnlyList<ScopeGroup> groups) =>
        [.. groups.Select(g => new TableRow(g.Kind?.ToString().ToLowerInvariant(), g.Id, g.Name,
            UsageTotals.Of([.. rows.Where(r => g.Users.Contains(r.UserId))], cost)))];

    private IReadOnlyList<TableRow> AgentRows(IReadOnlyCollection<UsageDay> rows) =>
        [.. rows.GroupBy(r => r.Agent).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new TableRow(null, g.Key, g.Key, UsageTotals.Of([.. g], cost)))];
}
