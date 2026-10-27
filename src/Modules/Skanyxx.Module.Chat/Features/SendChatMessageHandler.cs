using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features;

/// <summary>
/// Only a merged agent (by namespace and name), and only the caller's own conversation with that agent: kagent looks
/// sessions up by id and user, so another person's conversation id is "not found", and a conversation with a different
/// agent is refused the same way. A first turn that fails, or that the caller abandons, deletes the session it just
/// created, so retries leave no empty sessions behind.
/// With a reader, the chat index takes part (D236–D241): a new conversation's first message carries related prior work
/// (or is answered from the answer cache), and every completed turn updates the index.
/// </summary>
internal sealed class SendChatMessageHandler(KAgentApiClient kagent, IChatContext context, ILogger<SendChatMessageHandler> logger)
    : IRequestHandler<SendChatMessageCommand, Outcome<ChatResponse>>
{
    public async Task<Outcome<ChatResponse>> Handle(SendChatMessageCommand command, CancellationToken ct)
    {
        string? newSession = null;
        try
        {
            var agent = (await MergedAgents.ListAsync(kagent, ct))
                .FirstOrDefault(a => a.Namespace == command.AgentNamespace && a.Name == command.AgentName);
            if (agent is null)
                return Outcome<ChatResponse>.NotFound("No such agent to chat with.");

            var agentRef = $"{agent.Namespace}/{agent.Name}";
            var tickets = command.TicketKeys ?? [];
            var isNew = string.IsNullOrEmpty(command.ConversationId);
            var prepared = isNew && command.Reader is { } reader
                ? await context.PrepareAsync(new ChatTurnStart(reader, agentRef, agent.Revision, command.Message, tickets, command.NoCache), ct)
                : ChatPreparation.None;
            if (prepared.Cached is { } cached)
                return Outcome<ChatResponse>.Ok(cached);

            string sessionId;
            if (isNew)
                sessionId = newSession = (await kagent.CreateSessionAsync(agentRef, command.UserId, ct)).Id;
            else if (await kagent.GetSessionAsync(command.ConversationId!, command.UserId, ct) is { } session
                     && session.AgentId == MergedAgents.SessionAgentId(agent))
                sessionId = session.Id;
            else
                return Outcome<ChatResponse>.NotFound("No such conversation.");

            var response = await kagent.SendMessageAsync(
                agent.Namespace, agent.Name, sessionId, prepared.Context + command.Message, command.UserId, ct);
            response.RelatedSessions = prepared.Sessions;
            response.RelatedCards = prepared.Cards;
            // Not the request's token: the answer exists, so the index should record it even if the caller left.
            if (command.Reader is { } who)
                await context.CompletedAsync(new ChatTurnDone(
                    who, sessionId, agentRef, command.Message, response.Message.Content, isNew, tickets, prepared.CacheKey), CancellationToken.None);
            return Outcome<ChatResponse>.Ok(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Chat with {Namespace}/{Agent} failed for {UserId}", command.AgentNamespace, command.AgentName, command.UserId);
            if (newSession is not null)
                await DeleteAsync(newSession, command.UserId);
            return Outcome<ChatResponse>.Unavailable(ChatFailure.Message(ex));
        }
        catch (OperationCanceledException) when (newSession is not null && ct.IsCancellationRequested)
        {
            // The caller left (a closed tab) mid first turn: nobody will continue this conversation.
            await DeleteAsync(newSession, command.UserId);
            throw;
        }
    }

    // Not the request's token: the caller may be gone, the empty session should still go.
    private async Task DeleteAsync(string sessionId, string userId)
    {
        try
        {
            await kagent.DeleteSessionAsync(sessionId, userId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not delete the empty kagent session {SessionId}", sessionId);
        }
    }
}
