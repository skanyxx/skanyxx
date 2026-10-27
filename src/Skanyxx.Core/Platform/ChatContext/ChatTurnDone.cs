using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>A completed turn. <paramref name="Message"/> is what the person typed, without any injected context.</summary>
/// <param name="IsFirst">The turn that created the conversation.</param>
/// <param name="CacheKey">From <see cref="ChatPreparation.CacheKey"/>: where to keep this answer, when the cache is on.</param>
public sealed record ChatTurnDone(
    ChatReader Reader, string SessionId, string AgentRef, string Message, string Reply, bool IsFirst,
    IReadOnlyList<string> TicketKeys, string? CacheKey);
