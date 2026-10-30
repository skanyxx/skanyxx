namespace Skanyxx.Module.Analytics;

/// <summary>
/// <c>Analytics</c> configuration. The section's <c>Prices</c> and <c>Currency</c> are every module's model prices
/// (<see cref="Core.Platform.Usage.PricingOptions"/>, D549), not this module's alone.
/// </summary>
public sealed class AnalyticsOptions
{
    public const string Section = "Analytics";

    /// <summary>Filled from <c>ConnectionStrings:Memory</c>: the rollup lives in memory's database.</summary>
    public string ConnectionString { get; set; } = "";
}
