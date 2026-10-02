namespace SkanyxxWeb.Models;

public class ToolInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Available { get; set; }
    public string? Path { get; set; }
    public string? Version { get; set; }
}
