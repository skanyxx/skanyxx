namespace SkanyxxMaui.Models;

public class InvestigationStep
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AgentPrompt { get; set; } = string.Empty;
}
