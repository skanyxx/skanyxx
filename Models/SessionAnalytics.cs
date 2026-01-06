namespace SkanyxxWeb.Models;

public class SessionAnalytics
{
    public int TotalSessions { get; set; }
    public int TotalMessages { get; set; }
    public int TotalTokens { get; set; }
    public string AvgDuration { get; set; } = "0m";
    public decimal SessionsChangePercent { get; set; }
    public decimal MessagesChangePercent { get; set; }
    public decimal TokensChangePercent { get; set; }
    public decimal DurationChangePercent { get; set; }
}
