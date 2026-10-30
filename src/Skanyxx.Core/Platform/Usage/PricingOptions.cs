namespace Skanyxx.Core.Platform.Usage;

/// <summary>
/// The one place model prices come from (D549): configuration section <c>Analytics</c>, <see cref="Prices"/> keyed by
/// the ModelConfig as <c>namespace/name</c>, per million tokens in <see cref="Currency"/>. No price is ever assumed: a
/// model without one is "no price set", and its cost is not estimated.
/// </summary>
public sealed class PricingOptions
{
    public const string Section = "Analytics";

    public Dictionary<string, ModelPrice> Prices { get; set; } = [];

    public string Currency { get; set; } = "USD";
}
