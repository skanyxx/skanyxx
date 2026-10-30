namespace Skanyxx.Core.Platform.Usage;

/// <summary>
/// Model tokens times the configured price per model (<see cref="PricingOptions"/>), input and output apart; never a
/// guessed price. Analytics, the Dashboard, Quotas and Chat all estimate cost here (D549). Skanyxx's own count of what it
/// sent (client tokens) is never priced.
/// </summary>
public sealed class CostCalculator(PricingOptions options)
{
    public string Currency => options.Currency;

    public ModelPrice? PriceOf(string model) => options.Prices.GetValueOrDefault(model);

    /// <summary>Every configured price, by model.</summary>
    public IReadOnlyDictionary<string, ModelPrice> Prices => options.Prices;

    public CostEstimate Estimate(IEnumerable<ModelTokens> usage)
    {
        decimal amount = 0;
        var priced = false;
        var unpriced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var model in usage.GroupBy(u => u.Model))
        {
            if (PriceOf(model.Key) is not { } price)
            {
                if (model.Any(u => u.Input + u.Output > 0))
                    unpriced.Add(model.Key);
                continue;
            }
            priced = true;
            amount += model.Sum(u => u.Input) / 1_000_000m * price.InputPerMillion
                + model.Sum(u => u.Output) / 1_000_000m * price.OutputPerMillion;
        }
        return new CostEstimate(decimal.Round(amount, 4), options.Currency, priced, [.. unpriced]);
    }
}
