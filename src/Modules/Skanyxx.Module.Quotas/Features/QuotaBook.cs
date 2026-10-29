using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Everything one month's quotas are computed from, read once (<see cref="QuotaLedger.BookAsync"/>).</summary>
public sealed record QuotaBook(
    DateOnly Month, IReadOnlyList<ScopeLimit> Limits, IReadOnlyList<QuotaUsage> Usage, IReadOnlyDictionary<string, long> Extras,
    IReadOnlyDictionary<string, decimal> Prices)
{
    public IReadOnlyList<ScopeLimit> ApplicableTo(string userId, IEnumerable<string> teams, IEnumerable<string> departments) =>
        QuotaResolver.Applicable(Limits, userId, teams, departments);

    public PersonQuota For(string userId, IEnumerable<string> teams, IEnumerable<string> departments)
    {
        var rows = Usage.Where(u => u.UserId == userId).ToList();
        var priced = rows.Where(r => Prices.ContainsKey(r.Model)).ToList();
        decimal? cost = priced.Count == 0
            ? null
            : priced.Sum(r => (r.PromptTokens + r.CompletionTokens + r.EstimatedTokens) / 1000m * Prices[r.Model]);
        return new PersonQuota(userId,
            QuotaResolver.Resolve(ApplicableTo(userId, teams, departments), Extras.GetValueOrDefault(userId)),
            rows.Sum(r => r.PromptTokens), rows.Sum(r => r.CompletionTokens), rows.Sum(r => r.EstimatedTokens),
            rows.Sum(r => r.Turns), rows.Sum(r => r.EstimatedTurns),
            cost is { } c ? Math.Round(c, 4) : null,
            [.. rows.Select(r => r.Model).Where(m => !Prices.ContainsKey(m)).Distinct().Order(StringComparer.Ordinal)]);
    }
}
