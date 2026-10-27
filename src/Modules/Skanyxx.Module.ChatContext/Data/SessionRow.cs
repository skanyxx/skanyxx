namespace Skanyxx.Module.ChatContext.Data;

/// <summary>Dapper's shape for a row: it reads text[] as Array, which binds to properties but not to a record's constructor.</summary>
internal sealed class SessionRow
{
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string AgentRef { get; set; } = "";
    public string Scope { get; set; } = "";
    public string[] Keywords { get; set; } = [];
    public string[] Categories { get; set; } = [];
    public string[] TicketKeys { get; set; } = [];
    public string Outcome { get; set; } = "";
    public int Turns { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool Mine { get; set; }

    public SessionSummary ToSummary() =>
        new(SessionId, Title, AgentRef, Scope, Keywords, Categories, TicketKeys, Outcome, Turns, CreatedAt, UpdatedAt, Mine);
}
