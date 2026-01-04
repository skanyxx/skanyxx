namespace SkanyxxMaui.Models;

public class EventConfiguration
{
    public EventType EventType { get; set; }
    public AgentRef AgentRef { get; set; } = new();
    public string Prompt { get; set; } = string.Empty;
}
