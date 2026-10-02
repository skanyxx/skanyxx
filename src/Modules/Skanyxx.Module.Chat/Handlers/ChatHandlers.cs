using MediatR;
using Skanyxx.Core.Interfaces;
using Skanyxx.Core.Models;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Chat.Handlers;

public class SendChatMessageHandler : IRequestHandler<SendChatMessageCommand, ChatResponse>
{
    private readonly IChatService _svc;
    public SendChatMessageHandler(IChatService svc) => _svc = svc;
    public Task<ChatResponse> Handle(SendChatMessageCommand req, CancellationToken ct)
        => _svc.SendMessageAsync(new ChatRequest { Message = req.Message, AgentName = req.AgentName, ConversationId = req.ConversationId });
}
