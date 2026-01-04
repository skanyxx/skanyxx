namespace SkanyxxMaui.Models;

public class ChatMessage
{
    public string Id { get; set; } = string.Empty;
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
}
