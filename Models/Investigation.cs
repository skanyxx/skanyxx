namespace SkanyxxWeb.Models;

public class Investigation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "In Progress";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public List<string> Findings { get; set; } = new();
}
