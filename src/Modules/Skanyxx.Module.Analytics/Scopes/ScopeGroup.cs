namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>
/// A row of a report's table: a department, team or person under the scope, with the people it counts. A person in two
/// teams counts in both rows. <paramref name="Kind"/> null: not a scope of its own (the "not in a team" row).
/// </summary>
public sealed record ScopeGroup(ScopeKind? Kind, string Id, string Name, IReadOnlySet<string> Users);
