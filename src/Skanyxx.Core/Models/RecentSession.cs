namespace Skanyxx.Core.Models;

public class RecentSession
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime LastActivity { get; set; }
    public string Duration { get; set; } = string.Empty;
}
