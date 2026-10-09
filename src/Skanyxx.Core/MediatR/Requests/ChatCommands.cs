using MediatR;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;

namespace Skanyxx.Core.MediatR.Requests;

/// <summary>
/// <paramref name="UserId"/> sends <paramref name="Message"/> to the merged agent
/// <paramref name="AgentNamespace"/>/<paramref name="AgentName"/> through
/// kagent (D017, D4), in the conversation <paramref name="ConversationId"/> or a new one. kagent gets the user's id, so the
/// agent acts for that person (D5). NotFound for an agent that is not merged or a conversation that is not theirs.
/// </summary>
public record SendChatMessageCommand(string UserId, string Message, string AgentNamespace, string AgentName, string? ConversationId)
    : IRequest<Outcome<ChatResponse>>;
