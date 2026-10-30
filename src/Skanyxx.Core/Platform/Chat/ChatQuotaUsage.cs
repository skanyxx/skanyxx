using Skanyxx.Core.Platform.Usage;

namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// What one turn used, for the quota. <paramref name="Model"/> is the agent's ModelConfig as <c>namespace/name</c> (the
/// price key). <paramref name="Estimated"/>: kagent reported no tokens for the turn, so <paramref name="Usage"/> is the
/// caller's own estimate (<see cref="Measure"/>), and the quota shows it as one. <paramref name="ClientTokens"/> is
/// Skanyxx's own count of what it sent the agent (D548): shown, never counted against the limit or priced.
/// </summary>
public sealed record ChatQuotaUsage(
    string UserId, string AgentRef, string Model, string ConversationId, ChatUsage Usage, bool Estimated, long ClientTokens = 0)
{
    /// <summary>
    /// kagent's count when it reported one; otherwise the <paramref name="estimator"/>'s count of prompt and answer,
    /// marked as an estimate. Never nothing: a turn that reached kagent spent at least its prompt, also when it failed or
    /// was stopped before any answer (D380). <see cref="ClientTokens"/> is the prompt's count either way.
    /// </summary>
    public static ChatQuotaUsage Measure(
        ITokenEstimator estimator, string userId, string agentRef, string model, string conversationId, ChatUsage reported, string prompt, string answer)
    {
        var sent = estimator.Count(prompt, model);
        if (reported.Total > 0)
            return new(userId, agentRef, model, conversationId, reported, false, sent);
        var said = estimator.Count(answer, model);
        return new(userId, agentRef, model, conversationId, new ChatUsage(sent, said, sent + said), true, sent);
    }

    /// <summary>An agent's ModelConfig as the price key: <c>namespace/name</c>, its own namespace when it names none; <c>unknown</c> without one.</summary>
    public static string ModelRef(string agentNamespace, string? modelConfig) =>
        modelConfig switch { null or "" => "unknown", var m when m.Contains('/') => m, var m => $"{agentNamespace}/{m}" };
}
