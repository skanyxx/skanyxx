namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>What a report is about: the organization, a department or team (by slug), or a person (by user id).</summary>
public sealed record AnalyticsScope(ScopeKind Kind, string? Id)
{
    public static readonly AnalyticsScope Org = new(ScopeKind.Org, null);

    public static AnalyticsScope User(string id) => new(ScopeKind.User, id);
}
