namespace Skanyxx.Module.Quotas.Data;

/// <summary>The owner's price for 1,000 tokens (prompt and completion alike) on one ModelConfig (<c>namespace/name</c>), in USD.</summary>
public sealed class QuotaPrice
{
    public const string Currency = "USD";

    public string Model { get; set; } = "";
    public decimal PricePer1K { get; set; }
    public string SetBy { get; set; } = "";
    public DateTimeOffset SetAt { get; set; }
}
