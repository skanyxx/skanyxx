using SkanyxxWeb.Models;

namespace SkanyxxWeb.Interfaces;

public interface IChatService
{
    Task<ChatResponse> SendMessageAsync(ChatRequest request);
    Task<List<ChatMessage>> GetConversationAsync(string conversationId);
    Task DeleteConversationAsync(string conversationId);
}
