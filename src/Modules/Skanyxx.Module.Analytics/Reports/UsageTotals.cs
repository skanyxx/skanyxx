using Skanyxx.Module.Analytics.Data;

namespace Skanyxx.Module.Analytics.Reports;

/// <summary>
/// Sums over a set of rollup rows. <see cref="ActiveUsers"/>: people with at least one turn. <see cref="Tokens"/> and the
/// cost are the model's tokens; <see cref="ClientTokens"/> is what Skanyxx sent, by its own count (D548), never added in.
/// </summary>
public sealed record UsageTotals(
    long Requests, long Chats, long TokensIn, long TokensOut, long Unreported, long CacheHits, long TokensSaved, long Errors,
    int ActiveUsers, CostEstimate Cost, long ClientTokens)
{
    public long Tokens => TokensIn + TokensOut;

    /// <summary>Share of turns answered from the cache, 0–1; 0 with no turns.</summary>
    public double CacheHitRate => Requests == 0 ? 0 : (double)CacheHits / Requests;

    public static UsageTotals Of(IReadOnlyCollection<UsageDay> rows, CostCalculator cost) => new(
        rows.Sum(r => r.Requests), rows.Sum(r => r.Chats), rows.Sum(r => r.TokensIn), rows.Sum(r => r.TokensOut),
        rows.Sum(r => r.Unreported), rows.Sum(r => r.CacheHits), rows.Sum(r => r.TokensSaved), rows.Sum(r => r.Errors),
        rows.Where(r => r.Requests > 0).Select(r => r.UserId).Distinct().Count(), cost.Estimate(rows), rows.Sum(r => r.ClientTokens));
}
