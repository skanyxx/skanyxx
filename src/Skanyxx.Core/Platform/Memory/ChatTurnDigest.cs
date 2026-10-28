namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// One answered chat turn, as memory needs it to keep the session's card: what the person asked (without any injected
/// context) and what the agent answered. Memory keeps a line of each, never the transcript (D012, D050).
/// </summary>
/// <param name="AgentId">The agent's memory id: its kagent name.</param>
/// <param name="AgentRef"><c>namespace/name</c>.</param>
/// <param name="IsFirst">The turn that created the conversation: its question is the session's topic.</param>
public sealed record ChatTurnDigest(
    string UserId, string AgentId, string AgentRef, string SessionId, string Message, string Reply, bool IsFirst);
