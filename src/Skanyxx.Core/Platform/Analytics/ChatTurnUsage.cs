using MediatR;

namespace Skanyxx.Core.Platform.Analytics;

/// <summary>
/// One chat turn as a number, for the usage rollup (analytics module): who, which agent and model, the tokens, and
/// whether it opened a conversation, came from the answer cache (<paramref name="TokensSaved"/>: the estimate the cache
/// already uses, 4 characters a token) or failed. Never the message, the answer or anything else said.
/// </summary>
public sealed record ChatTurnUsage(
    DateTimeOffset At, string UserId, string AgentRef, string Model, TokenUsage Usage,
    bool NewChat, bool Cached, long TokensSaved, bool Failed) : INotification;
