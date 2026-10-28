namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>
/// A scope the viewer may open: its name, the people it counts (<see cref="Users"/> null = everyone, the organization)
/// and the groups one level down (none for a person: their table is by agent).
/// </summary>
public sealed record ResolvedScope(AnalyticsScope Scope, string Name, IReadOnlySet<string>? Users, IReadOnlyList<ScopeGroup> Children);
