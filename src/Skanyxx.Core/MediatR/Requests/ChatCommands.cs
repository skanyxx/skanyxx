using MediatR;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.MediatR.Requests;

public record SendChatMessageCommand(string Message, string AgentName, string? ConversationId) : IRequest<ChatResponse>;
