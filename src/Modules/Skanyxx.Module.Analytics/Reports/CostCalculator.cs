using Skanyxx.Module.Analytics.Data;

namespace Skanyxx.Module.Analytics.Reports;

/// <summary>Tokens times the owner's configured price per model (<see cref="AnalyticsOptions.Prices"/>); never a guessed price.</summary>
public sealed class CostCalculator(AnalyticsOptions options)
{
    public CostEstimate Estimate(IEnumerable<UsageDay> rows)
    {
        decimal amount = 0;
        var priced = false;
        var unpriced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var model in rows.GroupBy(r => r.Model))
        {
            if (!options.Prices.TryGetValue(model.Key, out var price))
            {
                if (model.Any(r => r.TokensIn + r.TokensOut > 0))
                    unpriced.Add(model.Key);
                continue;
            }
            priced = true;
            amount += model.Sum(r => r.TokensIn) / 1_000_000m * price.InputPerMillion
                + model.Sum(r => r.TokensOut) / 1_000_000m * price.OutputPerMillion;
        }
        return new CostEstimate(decimal.Round(amount, 4), options.Currency, priced, [.. unpriced]);
    }
}
