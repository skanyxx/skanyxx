namespace Skanyxx.Module.Quotas.Features;

/// <summary>Bounds every write is validated against.</summary>
public static class QuotaRules
{
    public const long MaxTokens = 1_000_000_000_000;
    public const decimal MaxPricePer1K = 1_000_000m;
    public const int MaxReason = 500;
    public const int MaxId = 450;
    public const int MaxModel = 320;
}
