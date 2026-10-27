using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>The first message of a new conversation, before kagent sees it.</summary>
/// <param name="AgentRef"><c>namespace/name</c>.</param>
/// <param name="AgentRevision">The Agent CR's resourceVersion: an edited agent never answers from an older one's cache.</param>
/// <param name="NoCache">The person asked for a fresh answer: skip the answer cache, read and write.</param>
public sealed record ChatTurnStart(
    ChatReader Reader, string AgentRef, string AgentRevision, string Message, IReadOnlyList<string> TicketKeys, bool NoCache);
