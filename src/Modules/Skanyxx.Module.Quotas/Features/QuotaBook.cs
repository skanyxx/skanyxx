using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// Everything one month's quotas are computed from, read once (<see cref="QuotaLedger.BookAsync"/>). Cost comes from the
/// configured prices (<see cref="CostCalculator"/>, the same as Analytics, D549), input and output apart: an estimated
/// turn's prompt as input, its answer as output. Only the model's tokens are priced.
/// </summary>
public sealed record QuotaBook(
    DateOnly Month, IReadOnlyList<ScopeLimit> Limits, IReadOnlyList<QuotaUsage> Usage, IReadOnlyDictionary<string, long> Extras,
    CostCalculator Cost)
{
    public IReadOnlyList<ScopeLimit> ApplicableTo(string userId, IEnumerable<string> teams, IEnumerable<string> departments) =>
        QuotaResolver.Applicable(Limits, userId, teams, departments);

    public PersonQuota For(string userId, IEnumerable<string> teams, IEnumerable<string> departments)
    {
        var rows = Usage.Where(u => u.UserId == userId).ToList();
        var cost = Cost.Estimate(rows.Select(r => new ModelTokens(r.Model,
            r.PromptTokens + r.EstimatedTokens - r.EstimatedCompletionTokens, r.CompletionTokens + r.EstimatedCompletionTokens)));
        return new PersonQuota(userId,
            QuotaResolver.Resolve(ApplicableTo(userId, teams, departments), Extras.GetValueOrDefault(userId)),
            rows.Sum(r => r.PromptTokens), rows.Sum(r => r.CompletionTokens), rows.Sum(r => r.EstimatedTokens),
            rows.Sum(r => r.Turns), rows.Sum(r => r.EstimatedTurns),
            cost.Priced ? cost.Amount : null,
            [.. rows.Select(r => r.Model).Where(m => Cost.PriceOf(m) is null).Distinct().Order(StringComparer.Ordinal)],
            rows.Sum(r => r.ClientTokens));
    }
}
