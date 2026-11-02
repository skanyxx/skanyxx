using MediatR;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Core.MediatR.Requests;

/// <summary>
/// <paramref name="UserId"/> sends <paramref name="Message"/> to an agent Chat offers (D557)
/// <paramref name="AgentNamespace"/>/<paramref name="AgentName"/> through
/// kagent (D017, D4), in the conversation <paramref name="ConversationId"/> or a new one. kagent gets the user's id, so the
/// agent acts for that person (D5). NotFound for an agent Chat does not offer or a conversation that is not theirs.
/// With <paramref name="Reader"/> (the signed-in person, for access checks), the chat index is used (D260–D265):
/// related prior work on the first turn, <paramref name="TicketKeys"/> linked, the index updated after the turn.
/// <paramref name="FileIds"/> are the caller's own uploaded chat files sent with this turn. A failed turn carries kagent's
/// own reason only with <paramref name="SeesFailureReason"/> (builders and up, <see cref="Caller.SeesAgentInternals"/>, D553).
/// </summary>
public record SendChatMessageCommand(
    string UserId, string Message, string AgentNamespace, string AgentName, string? ConversationId,
    ChatReader? Reader = null, IReadOnlyList<string>? TicketKeys = null, bool NoCache = false, IReadOnlyList<string>? FileIds = null,
    bool SeesFailureReason = false)
    : IRequest<Outcome<ChatResponse>>;
