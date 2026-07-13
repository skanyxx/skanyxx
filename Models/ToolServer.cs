namespace SkanyxxWeb.Models;

public class ToolServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Ref { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GroupKind { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int ToolCount => DiscoveredTools?.Count ?? 0;
    public string Status { get; set; } = "Online";
    public DateTime LastPing { get; set; } = DateTime.UtcNow;
    public List<DiscoveredTool> DiscoveredTools { get; set; } = new();
}

public class DiscoveredTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
