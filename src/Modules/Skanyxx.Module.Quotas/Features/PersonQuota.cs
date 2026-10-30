namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// One person's month: tokens reported by kagent (prompt, completion), Chat's estimates for turns kagent did not report,
/// the effective limit, and a cost estimate from the configured prices (null when no model they used has a price).
/// <see cref="Used"/>, the percentage and the cost are the model's tokens only; <see cref="ClientTokens"/> (what Skanyxx
/// sent, by its own count) is shown beside them and never counted (D548, D550).
/// </summary>
public sealed record PersonQuota(
    string UserId, EffectiveQuota Quota, long PromptTokens, long CompletionTokens, long EstimatedTokens, long Turns, long EstimatedTurns,
    decimal? Cost, IReadOnlyList<string> UnpricedModels, long ClientTokens)
{
    public long Used => PromptTokens + CompletionTokens + EstimatedTokens;

    public bool Over => Quota.Limit is { } limit && Used >= limit;

    /// <summary>Whole percent of the limit used (may pass 100: a turn already running may finish over it); null without a limit.</summary>
    public int? Percent => Quota.Limit switch
    {
        null => null,
        0 => 100,
        { } limit => (int)Math.Min(int.MaxValue, Used * 100 / limit)
    };
}
