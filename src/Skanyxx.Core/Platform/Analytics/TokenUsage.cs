namespace Skanyxx.Core.Platform.Analytics;

/// <summary>
/// The model-side tokens one chat turn spent, as kagent's runtime reported them (its <c>usage_metadata</c>: prompt and
/// tool-use prompt tokens in, candidate and reasoning tokens out). <see cref="Reported"/> is false when the answer
/// carried no usage at all: the turn happened, its tokens are unknown (never guessed).
/// </summary>
public sealed record TokenUsage(long InputTokens, long OutputTokens, bool Reported)
{
    public static readonly TokenUsage None = new(0, 0, false);
}
