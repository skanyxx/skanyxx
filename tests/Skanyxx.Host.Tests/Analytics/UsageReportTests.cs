using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Module.Analytics;
using Skanyxx.Module.Analytics.Data;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;
using static Skanyxx.Host.Tests.Analytics.OrgFixture;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>The rollup rows become the right sums, days, stacks, top lists, table rows and cost (with its disclaimer).</summary>
public sealed class UsageReportTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);
    private static readonly DateRange Week = DateRange.Last(7, Today);

    private static readonly CostCalculator Cost = new(new AnalyticsOptions
    {
        Prices = { ["kagent/gpt"] = new ModelPrice { InputPerMillion = 2m, OutputPerMillion = 8m } }
    });

    private static UsageDay Row(int daysAgo, string user, string agent, string model, long tokensIn, long tokensOut,
        long requests = 1, long chats = 0, long hits = 0, long saved = 0, long errors = 0, long unreported = 0) => new()
    {
        Day = Today.AddDays(-daysAgo), UserId = user, Agent = agent, Model = model, TokensIn = tokensIn, TokensOut = tokensOut,
        Requests = requests, Chats = chats, CacheHits = hits, TokensSaved = saved, Errors = errors, Unreported = unreported
    };

    private static readonly UsageDay[] Rows =
    [
        Row(0, "ann", "kagent/seed", "kagent/gpt", 1_000_000, 500_000, requests: 3, chats: 1),
        Row(0, "bob", "kagent/seed", "kagent/local", 200, 100, requests: 2, chats: 2, hits: 1, saved: 40),
        Row(2, "ann", "team-a/helper", "kagent/gpt", 0, 0, requests: 1, errors: 1),
        Row(6, "eve", "kagent/seed", "kagent/gpt", 10, 5, unreported: 1)
    ];

    private static UsageReport Org(UsageDay[]? previous = null) =>
        new UsageReportBuilder(Cost).Build(ScopeResolver.Resolve(Owner, AnalyticsScope.Org, OrgFixture.Snapshot).Value!, Week, Rows, previous, OrgFixture.Snapshot);

    [Fact]
    public void Totals_SumEveryRow()
    {
        var t = Org().Totals;

        Assert.Equal(7, t.Requests);
        Assert.Equal(3, t.Chats);
        Assert.Equal(1_000_210, t.TokensIn);
        Assert.Equal(500_105, t.TokensOut);
        Assert.Equal(1, t.CacheHits);
        Assert.Equal(40, t.TokensSaved);
        Assert.Equal(1, t.Errors);
        Assert.Equal(1, t.Unreported);
        Assert.Equal(3, t.ActiveUsers);
        Assert.Equal(1.0 / 7, t.CacheHitRate, 6);
    }

    [Fact]
    public void Days_RunFromTheFirstDayToToday_WithZeros()
    {
        var report = Org();

        Assert.Equal(7, report.Days.Count);
        Assert.Equal("2026-10-22", report.Days[0]);
        Assert.Equal("2026-10-28", report.Days[^1]);
        Assert.Equal([15L, 0, 0, 0, 0, 0, 1_500_300], report.TokensInPerDay.Zip(report.TokensOutPerDay, (i, o) => i + o));
        Assert.Equal([0L, 0, 0, 0, 1, 0, 0], report.ErrorsPerDay);
        Assert.Equal(40, report.SavedPerDay[^1]);
    }

    [Fact]
    public void Stacks_ByModelAndAgent_BiggestFirst()
    {
        var report = Org();

        Assert.Equal(["kagent/gpt", "kagent/local"], report.TokensByModel.Select(s => s.Name));
        Assert.Equal(1_500_000, report.TokensByModel[0].Values[^1]);
        Assert.Equal(["kagent/seed"], report.TokensByAgent.Select(s => s.Name)); // the helper used no tokens
        Assert.Equal(["kagent/seed"], report.ChatsByAgent.Select(s => s.Name));
    }

    [Fact]
    public void Stacks_FoldTheTailIntoOther()
    {
        var days = Week.Each().ToList();
        var many = Enumerable.Range(1, UsageReportBuilder.MaxSeries + 2).Select(i => Row(0, "ann", $"ns/agent-{i}", "m", i, 0)).ToList();

        var series = UsageReportBuilder.Stacked(many, days, r => r.Agent, r => r.TokensIn);

        Assert.Equal(UsageReportBuilder.MaxSeries + 1, series.Count);
        Assert.Equal("Other", series[^1].Name);
        Assert.Equal(1 + 2, series[^1].Values[^1]); // the two smallest
        Assert.Equal(many.Sum(r => r.TokensIn), series.Sum(s => s.Values[^1]));
    }

    [Fact]
    public void TopLists_AndTheTable_FollowTheScope()
    {
        var report = Org();

        Assert.Equal(["ann", "bob", "eve"], report.TopUsers.Select(u => u.Id));
        Assert.Equal(["billing", "payroll", "eu"], report.TopTeams.Select(t => t.Id));
        Assert.Equal(["Finance", "Sales", "Not in a team"], report.Rows.Select(r => r.Name));
        Assert.Equal([6L, 1, 0], report.Rows.Select(r => r.Totals.Requests)); // Finance = ann 4 + bob 2
        Assert.Equal(["department", "department", null], report.Rows.Select(r => r.Kind));
    }

    [Fact]
    public void ADepartment_RanksOnlyItsOwnTeams()
    {
        var finance = ScopeResolver.Resolve(Owner, new AnalyticsScope(ScopeKind.Department, "finance"), OrgFixture.Snapshot).Value!;

        var report = new UsageReportBuilder(Cost).Build(finance, Week, [.. Rows.Where(r => finance.Users!.Contains(r.UserId))], null, OrgFixture.Snapshot);

        Assert.Equal(["billing", "payroll"], report.TopTeams.Select(t => t.Id));
        Assert.Equal(["ann", "bob"], report.TopUsers.Select(u => u.Id));
    }

    [Fact]
    public void APerson_GetsTheirAgents_NoTopLists()
    {
        var ann = ScopeResolver.Resolve(Employee, AnalyticsScope.User("ann"), OrgFixture.Snapshot).Value!;
        var report = new UsageReportBuilder(Cost).Build(ann, Week, [.. Rows.Where(r => r.UserId == "ann")], null, OrgFixture.Snapshot);

        Assert.Empty(report.TopUsers);
        Assert.Empty(report.TopTeams);
        Assert.Equal(["kagent/seed", "team-a/helper"], report.Rows.Select(r => r.Name));
        Assert.All(report.Rows, r => Assert.Null(r.Kind));
    }

    [Fact]
    public void Compare_GivesThePreviousPeriodsTotals()
    {
        Assert.Null(Org().Previous);
        Assert.Equal(9, Org([Row(9, "ann", "kagent/seed", "kagent/gpt", 4, 5)]).Previous!.Tokens);
    }

    [Fact]
    public void Cost_IsOnlyForPricedModels_AndAlwaysCarriesTheDisclaimer()
    {
        var report = Org();
        var cost = report.Totals.Cost;

        // 1,000,010 in × 2/M + 500,005 out × 8/M (gpt); "kagent/local" has no price and is named, not guessed.
        Assert.True(cost.Priced);
        Assert.Equal(6.0001m, cost.Amount);
        Assert.Equal(["kagent/local"], cost.UnpricedModels);
        Assert.Equal(CostDisclaimer.Text, cost.Disclaimer);
        Assert.Equal(CostDisclaimer.Text, report.CostDisclaimer);
        Assert.Contains("not a bill", CostDisclaimer.Text);
        Assert.Contains("invoice is authoritative", CostDisclaimer.Text);

        var unpriced = new CostCalculator(new AnalyticsOptions()).Estimate(Rows);
        Assert.False(unpriced.Priced);
        Assert.Equal(0, unpriced.Amount);
    }
}
