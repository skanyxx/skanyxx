namespace SkanyxxWeb.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
