namespace SkanyxxWeb.Models;

public class MemoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}
