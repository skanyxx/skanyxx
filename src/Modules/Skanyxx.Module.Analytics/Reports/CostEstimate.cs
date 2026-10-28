using Skanyxx.Core.Platform.Analytics;
namespace Skanyxx.Module.Analytics.Reports;

/// <summary>
/// <paramref name="Amount"/> covers only the models with a price (<paramref name="Priced"/> false when none had one, and
/// the amount is then 0, not "free"); <paramref name="UnpricedModels"/> are the models used without one.
/// </summary>
public sealed record CostEstimate(decimal Amount, string Currency, bool Priced, IReadOnlyList<string> UnpricedModels)
{
    public string Disclaimer => CostDisclaimer.Text;
}
