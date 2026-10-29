namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// One person's month: tokens reported by kagent (prompt, completion), Chat's estimates for turns kagent did not report,
/// the effective limit, and a cost estimate from the owner's prices (null when no model they used has a price).
/// </summary>
public sealed record PersonQuota(
    string UserId, EffectiveQuota Quota, long PromptTokens, long CompletionTokens, long EstimatedTokens, long Turns, long EstimatedTurns,
    decimal? Cost, IReadOnlyList<string> UnpricedModels)
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
