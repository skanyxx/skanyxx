namespace SkanyxxWeb.Models;

public class ToolUsageStats
{
    public string ToolName { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public decimal Percentage { get; set; }
}
