namespace Skanyxx.Core.Platform.Chat;

/// <summary>Model tokens as kagent reports them (the provider's own counts, not an estimate).</summary>
public sealed record ChatUsage(long Prompt, long Completion, long Total)
{
    public static readonly ChatUsage None = new(0, 0, 0);

    public ChatUsage Add(ChatUsage other) => new(Prompt + other.Prompt, Completion + other.Completion, Total + other.Total);
}
