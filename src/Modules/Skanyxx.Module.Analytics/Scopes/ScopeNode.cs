namespace Skanyxx.Module.Analytics.Scopes;

/// <summary>One entry of the drill-down picker: a scope the viewer may open, and what they may open below it.</summary>
public sealed record ScopeNode(string Kind, string Id, string Name, IReadOnlyList<ScopeNode> Children)
{
    public ScopeNode(ScopeKind kind, string id, string name, IReadOnlyList<ScopeNode> children)
        : this(kind.ToString().ToLowerInvariant(), id, name, children)
    {
    }
}
