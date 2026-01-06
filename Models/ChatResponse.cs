namespace SkanyxxWeb.Models;

public class ChatResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public ChatMessage Message { get; set; } = new();
}
