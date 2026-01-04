namespace SkanyxxMaui.Models;

public class Investigation
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Agents { get; set; } = new();
    public string StartTime { get; set; } = string.Empty;
    public string? EndTime { get; set; }
    public InvestigationStatus Status { get; set; }
    public int CurrentStep { get; set; }
    public List<string>? Findings { get; set; }
    public List<string>? Recommendations { get; set; }
    public Dictionary<string, List<ChatMessage>>? AgentSessions { get; set; }
    public List<ChatMessage>? ChatMessages { get; set; }
}
