using MediatR;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Core.MediatR.Requests;

/// <summary>
/// <paramref name="UserId"/> sends <paramref name="Message"/> to the merged agent
/// <paramref name="AgentNamespace"/>/<paramref name="AgentName"/> through
/// kagent (D017, D4), in the conversation <paramref name="ConversationId"/> or a new one. kagent gets the user's id, so the
/// agent acts for that person (D5). NotFound for an agent that is not merged or a conversation that is not theirs.
/// With <paramref name="Reader"/> (the signed-in person, for access checks), the chat index is used (D260–D265):
/// related prior work on the first turn, <paramref name="TicketKeys"/> linked, the index updated after the turn.
/// <paramref name="FileIds"/> are the caller's own uploaded chat files sent with this turn.
/// </summary>
public record SendChatMessageCommand(
    string UserId, string Message, string AgentNamespace, string AgentName, string? ConversationId,
    ChatReader? Reader = null, IReadOnlyList<string>? TicketKeys = null, bool NoCache = false, IReadOnlyList<string>? FileIds = null)
    : IRequest<Outcome<ChatResponse>>;
