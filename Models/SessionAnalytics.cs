namespace SkanyxxWeb.Models;

public class SessionAnalytics
{
    // Aggregate dashboard stats
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public int TotalTokens { get; set; }
    public string AvgDuration { get; set; } = "0m";
    public decimal SessionsChangePercent { get; set; }
    public decimal MessagesChangePercent { get; set; }
    public decimal TokensChangePercent { get; set; }
    public decimal DurationChangePercent { get; set; }

    // Per-session fields
    public string? SessionId { get; set; }
    public long Duration { get; set; }
    public List<string> ToolsUsed { get; set; } = new();
    public decimal SuccessRate { get; set; }
    public string? CreatedAt { get; set; }
    public string? LastActivity { get; set; }
}
