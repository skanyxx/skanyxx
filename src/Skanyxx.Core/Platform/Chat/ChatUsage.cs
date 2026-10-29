using Skanyxx.Core.Platform.Analytics;

namespace Skanyxx.Core.Platform.Chat;

/// <summary>Model tokens as kagent reports them (the provider's own counts, not an estimate).</summary>
public sealed record ChatUsage(long Prompt, long Completion, long Total)
{
    public static readonly ChatUsage None = new(0, 0, 0);

    public ChatUsage Add(ChatUsage other) => new(Prompt + other.Prompt, Completion + other.Completion, Total + other.Total);

    /// <summary>A blocking answer's count (<see cref="Services.A2AUsage"/>); none when it reported none.</summary>
    public static ChatUsage Of(TokenUsage usage) =>
        usage.Reported ? new(usage.InputTokens, usage.OutputTokens, usage.InputTokens + usage.OutputTokens) : None;
}
