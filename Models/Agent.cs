namespace SkanyxxWeb.Models;

public class Agent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string Description { get; set; } = string.Empty;
    public string Namespace { get; set; } = "kagent";
    public bool Ready { get; set; }
    public bool Accepted { get; set; }
    public int TasksToday { get; set; }
    public int AvgResponseMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
