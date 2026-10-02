using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Chat.Services;

public class KAgentChatService : IChatService
{
    private readonly KAgentApiClient _kagent;
    private readonly ILogger<KAgentChatService> _logger;

    public KAgentChatService(KAgentApiClient kagent, ILogger<KAgentChatService> logger)
    {
        _kagent = kagent;
        _logger = logger;
    }

    public async Task<ChatResponse> SendMessageAsync(ChatRequest request)
    {
        try
        {
            var sessionId = request.ConversationId;
            if (string.IsNullOrEmpty(sessionId))
            {
                var agentName = string.IsNullOrEmpty(request.AgentName) ? "default-agent" : request.AgentName;
                var session = await _kagent.CreateSessionAsync(agentName);
                sessionId = session.Id;
            }
            return await _kagent.SendMessageAsync(sessionId, request.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to agent {Agent}", request.AgentName);
            throw;
        }
    }

    public async Task<List<ChatMessage>> GetConversationAsync(string conversationId)
    {
        try { return await _kagent.GetSessionMessagesAsync(conversationId); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get conversation {Id}", conversationId);
            return new List<ChatMessage>();
        }
    }

    public async Task DeleteConversationAsync(string conversationId)
    {
        try { await _kagent.DeleteSessionAsync(conversationId); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete conversation {Id}", conversationId); }
    }
}
