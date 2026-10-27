namespace Skanyxx.Module.ChatContext.Data;

/// <summary>One index row as the API shows it. <paramref name="Mine"/>: the reader owns it.</summary>
public sealed record SessionSummary(
    string SessionId, string Title, string AgentRef, string Scope, string[] Keywords, string[] Categories, string[] TicketKeys,
    string Outcome, int Turns, DateTime CreatedAt, DateTime UpdatedAt, bool Mine);
