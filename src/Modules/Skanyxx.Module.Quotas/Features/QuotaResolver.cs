using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// Which limit binds a person (D332): every limit set on them, on any of their teams and on those teams' departments
/// applies, and the smallest wins; on a tie the narrower scope is named (person, then team, then department). Approved
/// requests add to whatever binds.
/// </summary>
public static class QuotaResolver
{
    public static IReadOnlyList<ScopeLimit> Applicable(
        IEnumerable<ScopeLimit> all, string userId, IEnumerable<string> teams, IEnumerable<string> departments)
    {
        var teamSet = teams.ToHashSet();
        var departmentSet = departments.ToHashSet();
        return [.. all.Where(l => l.Kind switch
        {
            QuotaScopes.User => l.Id == userId,
            QuotaScopes.Team => teamSet.Contains(l.Id),
            QuotaScopes.Department => departmentSet.Contains(l.Id),
            _ => false
        })];
    }

    public static EffectiveQuota Resolve(IEnumerable<ScopeLimit> applicable, long extra) =>
        Tightest(applicable) is { } binding ? new EffectiveQuota(binding.MonthlyTokens + extra, binding, extra) : EffectiveQuota.Unlimited;

    /// <summary>
    /// The most a supervisor may let a person have (D339): their tightest team or department limit; with none, their
    /// personal limit as it stands (a supervisor may only lower it; raising or removing it is the owner's); with neither,
    /// null: the person is unlimited, so nothing a supervisor does can give them more.
    /// </summary>
    public static long? SupervisorCap(IReadOnlyCollection<ScopeLimit> applicable) =>
        Tightest(applicable.Where(l => l.Kind != QuotaScopes.User))?.MonthlyTokens
        ?? applicable.FirstOrDefault(l => l.Kind == QuotaScopes.User)?.MonthlyTokens;

    private static ScopeLimit? Tightest(IEnumerable<ScopeLimit> limits) =>
        limits.OrderBy(l => l.MonthlyTokens).ThenBy(l => Rank(l.Kind)).ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault();

    private static int Rank(string kind) => kind switch { QuotaScopes.User => 0, QuotaScopes.Team => 1, _ => 2 };
}
