using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Scopes;
using static Skanyxx.Host.Tests.Analytics.OrgFixture;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>requirements.md §3.11: a user sees only themselves; a supervisor their teams; the owner everything.</summary>
public sealed class ScopeResolverTests
{
    private static Outcome<ResolvedScope> Resolve(AnalyticsViewer viewer, ScopeKind kind, string? id) =>
        ScopeResolver.Resolve(viewer, new AnalyticsScope(kind, id), Snapshot);

    [Fact]
    public void AUser_SeesOnlyThemselves()
    {
        Assert.Equal(["ann"], Resolve(Employee, ScopeKind.User, "ann").Value!.Users!);
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Employee, ScopeKind.User, "sam").Status); // a teammate
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Employee, ScopeKind.User, "nobody").Status); // no existence leak
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Employee, ScopeKind.Team, "billing").Status); // their own team
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Employee, ScopeKind.Department, "finance").Status);
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Employee, ScopeKind.Org, null).Status);
    }

    [Fact]
    public void ASupervisor_SeesTheirTeams_AndThePeopleInThem_NotWider()
    {
        var team = Resolve(Supervisor, ScopeKind.Team, "billing").Value!;
        Assert.Equal(["ann", "sam"], team.Users!.Order());
        Assert.Equal(["ann", "sam"], team.Children.Select(c => c.Id).Order());
        Assert.NotNull(Resolve(Supervisor, ScopeKind.User, "ann").Value);

        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Supervisor, ScopeKind.Team, "payroll").Status); // same department, not their team
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Supervisor, ScopeKind.User, "bob").Status);
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Supervisor, ScopeKind.Department, "finance").Status);
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Supervisor, ScopeKind.Org, null).Status);
        Assert.Equal(OutcomeStatus.Forbidden, Resolve(Supervisor, ScopeKind.Team, "no-such-team").Status);
    }

    [Fact]
    public void TheOwner_DrillsDown_OrganizationDepartmentTeamPerson()
    {
        var org = Resolve(Owner, ScopeKind.Org, null).Value!;
        Assert.Null(org.Users); // everyone
        Assert.Equal(["Finance", "Sales", "Not in a team"], org.Children.Select(c => c.Name));
        Assert.Equal(["nora", "olga"], org.Children[^1].Users.Order());

        var finance = Resolve(Owner, ScopeKind.Department, "finance").Value!;
        Assert.Equal(["ann", "bob", "sam"], finance.Users!.Order());
        Assert.Equal(["billing", "payroll"], finance.Children.Select(c => c.Id));

        Assert.Equal(["bob"], Resolve(Owner, ScopeKind.Team, "payroll").Value!.Users!);
        Assert.Equal(["eve"], Resolve(Owner, ScopeKind.User, "eve").Value!.Users!);
        Assert.Equal(OutcomeStatus.NotFound, Resolve(Owner, ScopeKind.Team, "no-such-team").Status);
    }

    [Fact]
    public void TheTree_OffersOnlyWhatOpens()
    {
        Assert.Equal(["Me"], ScopeResolver.Tree(Employee, Snapshot).Select(n => n.Name));
        Assert.Equal(["Me", "Billing"], ScopeResolver.Tree(Supervisor, Snapshot).Select(n => n.Name));
        var owner = ScopeResolver.Tree(Owner, Snapshot);
        Assert.Equal(["user", "org"], owner.Select(n => n.Kind));
        Assert.Equal(["Finance", "Sales"], owner[1].Children.Select(n => n.Name));
    }
}
