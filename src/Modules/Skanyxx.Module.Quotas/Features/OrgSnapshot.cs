using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>The org tree as identity has it right now, read once per request.</summary>
public sealed record OrgSnapshot(IReadOnlyList<PersonDto> People, IReadOnlyList<TeamDto> Teams, IReadOnlyList<DepartmentDto> Departments)
{
    public PersonDto? Person(string userId) => People.FirstOrDefault(p => p.Id == userId);

    public string PersonName(string userId) => Person(userId) is { } p ? p.DisplayName ?? p.Email : userId;

    public IReadOnlyList<TeamDto> TeamsOf(string userId) => [.. Teams.Where(t => t.Members.Contains(userId))];

    public IReadOnlyList<string> DepartmentsOf(string userId) => [.. TeamsOf(userId).Select(t => t.Department).Distinct()];

    /// <summary>Who a supervisor looks after: the members of the teams they are in (as Analytics, D332).</summary>
    public IReadOnlySet<string> SupervisedBy(string userId) => TeamsOf(userId).SelectMany(t => t.Members).ToHashSet();

    public string ScopeName(string kind, string id) => kind switch
    {
        Data.QuotaScopes.User => PersonName(id),
        Data.QuotaScopes.Team => Teams.FirstOrDefault(t => t.Slug == id)?.Name ?? id,
        _ => Departments.FirstOrDefault(d => d.Slug == id)?.Name ?? id
    };

    public bool Exists(string kind, string id) => kind switch
    {
        Data.QuotaScopes.User => Person(id) is not null,
        Data.QuotaScopes.Team => Teams.Any(t => t.Slug == id),
        _ => Departments.Any(d => d.Slug == id)
    };
}
