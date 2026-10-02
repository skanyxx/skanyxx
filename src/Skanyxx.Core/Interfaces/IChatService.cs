using Skanyxx.Core.Models;

namespace Skanyxx.Core.Interfaces;

public interface IChatService
{
    Task<ChatResponse> SendMessageAsync(ChatRequest request);
    Task<List<ChatMessage>> GetConversationAsync(string conversationId);
    Task DeleteConversationAsync(string conversationId);
}
