namespace SkanyxxMaui.Models;

public class ToolServer
{
    public string Ref { get; set; } = string.Empty;
    public string GroupKind { get; set; } = string.Empty;
    public List<DiscoveredTool> DiscoveredTools { get; set; } = new();
}
