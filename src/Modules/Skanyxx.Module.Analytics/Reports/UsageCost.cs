using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Analytics.Data;

namespace Skanyxx.Module.Analytics.Reports;

/// <summary>The rollup's model tokens priced by the shared <see cref="CostCalculator"/> (D549); client tokens never are.</summary>
public static class UsageCost
{
    public static CostEstimate Estimate(this CostCalculator cost, IEnumerable<UsageDay> rows) =>
        cost.Estimate(rows.Select(r => new ModelTokens(r.Model, r.TokensIn, r.TokensOut)));
}
