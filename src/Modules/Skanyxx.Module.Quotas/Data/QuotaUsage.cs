namespace Skanyxx.Module.Quotas.Data;

/// <summary>
/// One person's tokens on one model in one UTC month. <see cref="EstimatedTokens"/> are turns kagent reported no usage for
/// (Chat's estimate); they count against the quota like the reported ones and are shown as estimates.
/// </summary>
public sealed class QuotaUsage
{
    public DateOnly Month { get; set; }
    public string UserId { get; set; } = "";
    public string Model { get; set; } = "";
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long EstimatedTokens { get; set; }
    public long Turns { get; set; }
    public long EstimatedTurns { get; set; }
}
