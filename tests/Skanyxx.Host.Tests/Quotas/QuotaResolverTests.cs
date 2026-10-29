using Skanyxx.Module.Quotas.Data;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Host.Tests.Quotas;

/// <summary>Which limit binds a person (D332): every applicable one counts, the smallest wins, approved extras add to it.</summary>
public sealed class QuotaResolverTests
{
    private static readonly ScopeLimit[] All =
    [
        new(QuotaScopes.User, "ann", 800),
        new(QuotaScopes.User, "bob", 50),
        new(QuotaScopes.Team, "billing", 500),
        new(QuotaScopes.Team, "payroll", 10),
        new(QuotaScopes.Department, "finance", 1000),
        new(QuotaScopes.Department, "sales", 1),
    ];

    private static IReadOnlyList<ScopeLimit> Ann(params string[] teams) =>
        QuotaResolver.Applicable(All, "ann", teams, teams.Length == 0 ? [] : ["finance"]);

    [Fact]
    public void TheTightestApplicableLimit_Binds_AndOthersPeoplesTeamsDoNotApply()
    {
        var quota = QuotaResolver.Resolve(Ann("billing"), extra: 0);

        Assert.Equal(500, quota.Limit);
        Assert.Equal(new ScopeLimit(QuotaScopes.Team, "billing", 500), quota.Binding);
        Assert.Equal(["ann", "billing", "finance"], Ann("billing").Select(l => l.Id));
    }

    [Fact]
    public void APersonalLimit_BindsWhenItIsTheTightest_AndADepartmentWhenOnlyItIsSet()
    {
        Assert.Equal(QuotaScopes.User, QuotaResolver.Resolve(Ann(), 0).Binding!.Kind);
        Assert.Equal(800, QuotaResolver.Resolve(Ann(), 0).Limit);

        var dept = QuotaResolver.Resolve(QuotaResolver.Applicable(All, "carl", ["audit"], ["finance"]), 0);
        Assert.Equal(new ScopeLimit(QuotaScopes.Department, "finance", 1000), dept.Binding);
    }

    [Fact]
    public void OnATie_TheNarrowerScopeIsNamed()
    {
        ScopeLimit[] tie = [new(QuotaScopes.Department, "finance", 300), new(QuotaScopes.Team, "billing", 300), new(QuotaScopes.User, "ann", 300)];

        Assert.Equal(QuotaScopes.User, QuotaResolver.Resolve(tie, 0).Binding!.Kind);
        Assert.Equal(QuotaScopes.Team, QuotaResolver.Resolve(tie[..2], 0).Binding!.Kind);
    }

    [Fact]
    public void ApprovedExtras_AddToTheBindingLimit_AndNoLimitStaysNoLimit()
    {
        Assert.Equal(1500, QuotaResolver.Resolve(Ann("billing"), extra: 1000).Limit);
        Assert.Equal(1000, QuotaResolver.Resolve(Ann("billing"), extra: 1000).Extra);
        Assert.Null(QuotaResolver.Resolve([], extra: 1000).Limit);
    }

    [Fact]
    public void ASupervisorsCap_IsTheTightestTeamOrDepartmentLimit_ElseThePersonalOne_ElseNone()
    {
        Assert.Equal(500, QuotaResolver.SupervisorCap(Ann("billing")));
        Assert.Equal(5, QuotaResolver.SupervisorCap([new ScopeLimit(QuotaScopes.User, "ann", 5)]));
        Assert.Null(QuotaResolver.SupervisorCap([]));
    }

    [Fact]
    public void Percent_IsOfTheEffectiveLimit_AndPassesAHundredWhenOver()
    {
        var quota = new EffectiveQuota(200, new ScopeLimit(QuotaScopes.User, "ann", 200), 0);
        var over = new PersonQuota("ann", quota, 120, 30, 150, 3, 1, null, []);

        Assert.Equal(300, over.Used);
        Assert.Equal(150, over.Percent);
        Assert.True(over.Over);
        Assert.Null(new PersonQuota("ann", EffectiveQuota.Unlimited, 1, 1, 0, 1, 0, null, []).Percent);
    }
}
