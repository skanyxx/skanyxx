using System.Security.Claims;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.Chat.Endpoints;

/// <param name="TicketKeys">Jira keys this conversation is about (e.g. started on SDB-58), linked in the chat index.</param>
/// <param name="NoCache">Ask kagent even when the answer cache holds this question.</param>
/// <param name="FileIds">The caller's own uploaded files to send with this message.</param>
internal sealed record SendChatRequest(
    string? Message, string? AgentNamespace, string? AgentName, string? ConversationId, string[]? TicketKeys = null, bool NoCache = false,
    string[]? FileIds = null)
{
    /// <summary>As the signed-in person, never a user id from the body.</summary>
    public SendChatMessageCommand Command(ClaimsPrincipal user) => new(
        Caller.UserId(user)!, Message ?? "", AgentNamespace?.Trim() ?? "", AgentName?.Trim() ?? "", ConversationId,
        ChatReader.From(user), TicketKeys ?? [], NoCache, FileIds ?? [], Caller.SeesAgentInternals(user));
}
