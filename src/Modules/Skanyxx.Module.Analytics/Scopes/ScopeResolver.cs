using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>
/// Who may see which analytics (requirements.md §3.11): everyone their own; the owner every person, team, department and
/// the organization; a supervisor (maintainer) the teams they are a member of and the people in them, never a department
/// or the organization. Anything else is Forbidden, whether it exists or not, so a refusal does not reveal a slug or id.
/// </summary>
public static class ScopeResolver
{
    public static Outcome<ResolvedScope> Resolve(AnalyticsViewer viewer, AnalyticsScope scope, OrgSnapshot org) => scope.Kind switch
    {
        ScopeKind.User when scope.Id == viewer.UserId || scope.Id is null => Person(viewer.UserId, org),
        ScopeKind.User when viewer.IsOwner || (viewer.IsSupervisor && SupervisedPeople(viewer, org).Contains(scope.Id!)) =>
            org.People.Any(p => p.Id == scope.Id) ? Person(scope.Id!, org) : NotFound(viewer),
        ScopeKind.Team when viewer.IsOwner || (viewer.IsSupervisor && org.TeamsOf(viewer.UserId).Any(t => t.Slug == scope.Id)) =>
            org.Teams.FirstOrDefault(t => t.Slug == scope.Id) is { } team
                ? Ok(scope, team.Name, team.Members.ToHashSet(),
                    [.. team.Members.Select(m => new ScopeGroup(ScopeKind.User, m, org.PersonName(m), new HashSet<string> { m })).OrderBy(g => g.Name)])
                : NotFound(viewer),
        ScopeKind.Department when viewer.IsOwner =>
            org.Departments.FirstOrDefault(d => d.Slug == scope.Id) is { } department
                ? Ok(scope, department.Name, org.MembersOf(org.Teams.Where(t => t.Department == department.Slug)),
                    [.. org.Teams.Where(t => t.Department == department.Slug)
                        .Select(t => new ScopeGroup(ScopeKind.Team, t.Slug, t.Name, t.Members.ToHashSet())).OrderBy(g => g.Name)])
                : NotFound(viewer),
        ScopeKind.Org when viewer.IsOwner => Ok(scope, "Organization", null, OrgGroups(org)),
        _ => Outcome<ResolvedScope>.Forbidden("These analytics are not yours to see.")
    };

    /// <summary>The scopes the viewer may open, for the drill-down picker.</summary>
    public static IReadOnlyList<ScopeNode> Tree(AnalyticsViewer viewer, OrgSnapshot org)
    {
        var me = new ScopeNode(ScopeKind.User, viewer.UserId, "Me", []);
        if (viewer.IsOwner)
            return [me, new ScopeNode(ScopeKind.Org, "", "Organization",
                [.. org.Departments.OrderBy(d => d.Name).Select(d => new ScopeNode(ScopeKind.Department, d.Slug, d.Name,
                    [.. org.Teams.Where(t => t.Department == d.Slug).OrderBy(t => t.Name).Select(t => TeamNode(t, org))]))])];
        if (viewer.IsSupervisor)
            return [me, .. org.TeamsOf(viewer.UserId).OrderBy(t => t.Name).Select(t => TeamNode(t, org))];
        return [me];
    }

    private static ScopeNode TeamNode(Core.Platform.Identity.TeamDto team, OrgSnapshot org) =>
        new(ScopeKind.Team, team.Slug, team.Name,
            [.. team.Members.Select(m => new ScopeNode(ScopeKind.User, m, org.PersonName(m), [])).OrderBy(n => n.Name)]);

    private static IReadOnlySet<string> SupervisedPeople(AnalyticsViewer viewer, OrgSnapshot org) =>
        org.MembersOf(org.TeamsOf(viewer.UserId));

    private static IReadOnlyList<ScopeGroup> OrgGroups(OrgSnapshot org)
    {
        List<ScopeGroup> groups =
        [
            .. org.Departments.OrderBy(d => d.Name).Select(d => new ScopeGroup(ScopeKind.Department, d.Slug, d.Name,
                org.MembersOf(org.Teams.Where(t => t.Department == d.Slug))))
        ];
        var inTeams = org.MembersOf(org.Teams);
        var loose = org.People.Select(p => p.Id).Where(id => !inTeams.Contains(id)).ToHashSet();
        if (loose.Count > 0)
            groups.Add(new ScopeGroup(null, "", "Not in a team", loose));
        return groups;
    }

    private static Outcome<ResolvedScope> Person(string userId, OrgSnapshot org) =>
        Ok(AnalyticsScope.User(userId), org.PersonName(userId), new HashSet<string> { userId }, []);

    // Only the owner learns that something does not exist; anyone else was already refused above.
    private static Outcome<ResolvedScope> NotFound(AnalyticsViewer viewer) =>
        viewer.IsOwner ? Outcome<ResolvedScope>.NotFound("No such scope.") : Outcome<ResolvedScope>.Forbidden("These analytics are not yours to see.");

    private static Outcome<ResolvedScope> Ok(AnalyticsScope scope, string name, IReadOnlySet<string>? users, IReadOnlyList<ScopeGroup> children) =>
        Outcome<ResolvedScope>.Ok(new ResolvedScope(scope, name, users, children));
}
