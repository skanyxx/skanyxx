namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// The scopes a search may look in: <paramref name="Exact"/> scope ids, plus every <c>team:</c> and <c>department:</c>
/// scope when <paramref name="AllTeamsAndDepartments"/> (owner/supervisor oversight, whether or not an org object exists).
/// </summary>
public sealed record SearchScopes(IReadOnlyList<string> Exact, bool AllTeamsAndDepartments)
{
    public bool Includes(Scope scope) =>
        Exact.Contains(scope.ToString()) || (AllTeamsAndDepartments && scope.Level is ScopeLevel.Team or ScopeLevel.Department);
}
