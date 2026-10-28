namespace Skanyxx.Module.Analytics;

/// <summary>
/// <c>Analytics</c> configuration. <see cref="Prices"/> are the owner's own per-model prices (key: the ModelConfig as
/// <c>namespace/name</c>), per million tokens in <see cref="Currency"/>. No price is ever assumed: a model without one is
/// "no price set", and its cost is not estimated.
/// </summary>
public sealed class AnalyticsOptions
{
    public const string Section = "Analytics";

    public Dictionary<string, ModelPrice> Prices { get; set; } = [];

    public string Currency { get; set; } = "USD";

    /// <summary>Filled from <c>ConnectionStrings:Memory</c>: the rollup lives in memory's database.</summary>
    public string ConnectionString { get; set; } = "";
}
