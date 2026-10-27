namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>A prior chat used as context. <paramref name="Mine"/>: the reader's own; otherwise shared with them.</summary>
public sealed record RelatedSession(
    string SessionId, string Title, string AgentRef, string Outcome, IReadOnlyList<string> TicketKeys, DateTime UpdatedAt, bool Mine);
