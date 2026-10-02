namespace Skanyxx.Core.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public string AgentName { get; set; } = "default-agent";
}
