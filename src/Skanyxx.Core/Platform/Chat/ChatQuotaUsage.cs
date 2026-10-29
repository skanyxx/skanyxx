namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// What one turn used, for the quota. <paramref name="Model"/> is the agent's ModelConfig as <c>namespace/name</c> (the
/// price key). <paramref name="Estimated"/>: kagent reported no tokens for the turn, so <paramref name="Usage"/> is the
/// caller's own estimate (<see cref="Measure"/>), and the quota shows it as one.
/// </summary>
public sealed record ChatQuotaUsage(string UserId, string AgentRef, string Model, string ConversationId, ChatUsage Usage, bool Estimated)
{
    /// <summary>The answer cache's estimate too (question plus answer, 4 characters a token).</summary>
    public const int CharsPerToken = 4;

    /// <summary>
    /// kagent's count when it reported one; otherwise prompt plus answer, <see cref="CharsPerToken"/> characters a token
    /// (rounded up), marked as an estimate. Never nothing: a turn that reached kagent spent at least its prompt, also when
    /// it failed or was stopped before any answer (D380).
    /// </summary>
    public static ChatQuotaUsage Measure(string userId, string agentRef, string model, string conversationId, ChatUsage reported, string prompt, string answer) =>
        reported.Total > 0
            ? new(userId, agentRef, model, conversationId, reported, false)
            : new(userId, agentRef, model, conversationId,
                new ChatUsage(Tokens(prompt.Length), Tokens(answer.Length), Tokens(prompt.Length + answer.Length)), true);

    /// <summary>An agent's ModelConfig as the price key: <c>namespace/name</c>, its own namespace when it names none; <c>unknown</c> without one.</summary>
    public static string ModelRef(string agentNamespace, string? modelConfig) =>
        modelConfig switch { null or "" => "unknown", var m when m.Contains('/') => m, var m => $"{agentNamespace}/{m}" };

    private static long Tokens(int chars) => (chars + CharsPerToken - 1) / CharsPerToken;
}
