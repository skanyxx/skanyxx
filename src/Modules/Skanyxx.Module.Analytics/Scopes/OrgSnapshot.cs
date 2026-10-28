using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>The org tree as identity has it right now (people, teams, departments), read once per report.</summary>
public sealed record OrgSnapshot(IReadOnlyList<PersonDto> People, IReadOnlyList<TeamDto> Teams, IReadOnlyList<DepartmentDto> Departments)
{
    public string PersonName(string userId) =>
        People.FirstOrDefault(p => p.Id == userId) is { } person ? person.DisplayName ?? person.Email : userId;

    public IEnumerable<TeamDto> TeamsOf(string userId) => Teams.Where(t => t.Members.Contains(userId));

    public IReadOnlySet<string> MembersOf(IEnumerable<TeamDto> teams) => teams.SelectMany(t => t.Members).ToHashSet();
}
