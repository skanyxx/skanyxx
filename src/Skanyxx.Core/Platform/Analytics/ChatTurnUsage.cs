using MediatR;

namespace Skanyxx.Core.Platform.Analytics;

/// <summary>
/// One chat turn as a number, for the usage rollup (analytics module): who, which agent and model, the tokens, and
/// whether it opened a conversation, came from the answer cache (<paramref name="TokensSaved"/>: the estimate the cache
/// already uses, the shared token estimate) or failed. Never the message, the answer or anything else said.
/// <paramref name="Usage"/> is the model's tokens (kagent's report); <paramref name="ClientTokens"/> is Skanyxx's own
/// estimate of what it sent the agent (D548), kept apart and never added to them.
/// </summary>
public sealed record ChatTurnUsage(
    DateTimeOffset At, string UserId, string AgentRef, string Model, TokenUsage Usage,
    bool NewChat, bool Cached, long TokensSaved, bool Failed, long ClientTokens = 0) : INotification;
