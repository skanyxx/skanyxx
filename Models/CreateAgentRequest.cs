namespace SkanyxxWeb.Models;

public class CreateAgentRequest
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Declarative";
    public string Namespace { get; set; } = "kagent";
    public string Description { get; set; } = string.Empty;
    public string ModelConfig { get; set; } = "default-anthropic";
    public List<string> ToolServers { get; set; } = new();
    public string SystemPrompt { get; set; } = string.Empty;
}
