namespace Skanyxx.Module.Analytics.Data;

/// <summary>
/// One person's chat turns with one agent on one model, on one UTC day: counts and tokens only. <see cref="Unreported"/>
/// counts turns whose tokens kagent did not report (their tokens are not in the sums). <see cref="Requests"/> includes
/// cached and failed turns. <see cref="TokensIn"/> and <see cref="TokensOut"/> are the model's (kagent's report);
/// <see cref="ClientTokens"/> is Skanyxx's own estimate of what it sent (D548), never added to them.
/// </summary>
public sealed class UsageDay
{
    public DateOnly Day { get; set; }
    public string UserId { get; set; } = "";
    public string Agent { get; set; } = "";
    public string Model { get; set; } = "";
    public long Requests { get; set; }
    public long Chats { get; set; }
    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public long ClientTokens { get; set; }
    public long Unreported { get; set; }
    public long CacheHits { get; set; }
    public long TokensSaved { get; set; }
    public long Errors { get; set; }
}
