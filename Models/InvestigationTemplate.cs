namespace SkanyxxMaui.Models;

public class InvestigationTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public List<string> DefaultAgents { get; set; } = new();
    public List<InvestigationStep> Steps { get; set; } = new();
}
