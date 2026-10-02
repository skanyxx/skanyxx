namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Team slugs the person is a member of, and the slugs of the departments those teams are in (department membership
/// is derived from teams, never assigned directly).
/// </summary>
public sealed record OrgMembership(IReadOnlySet<string> Teams, IReadOnlySet<string> Departments);
