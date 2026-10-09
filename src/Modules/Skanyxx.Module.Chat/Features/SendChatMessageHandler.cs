using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features;

/// <summary>
/// Only a merged agent (by namespace and name), and only the caller's own conversation with that agent: kagent looks
/// sessions up by id and user, so another person's conversation id is "not found", and a conversation with a different
/// agent is refused the same way. A first turn that fails, or that the caller abandons, deletes the session it just
/// created, so retries leave no empty sessions behind.
/// </summary>
internal sealed class SendChatMessageHandler(KAgentApiClient kagent, ILogger<SendChatMessageHandler> logger)
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

            string sessionId;
            if (string.IsNullOrEmpty(command.ConversationId))
                sessionId = newSession = (await kagent.CreateSessionAsync($"{agent.Namespace}/{agent.Name}", command.UserId, ct)).Id;
            else if (await kagent.GetSessionAsync(command.ConversationId, command.UserId, ct) is { } session
                     && session.AgentId == MergedAgents.SessionAgentId(agent))
                sessionId = session.Id;
            else
                return Outcome<ChatResponse>.NotFound("No such conversation.");

            return Outcome<ChatResponse>.Ok(
                await kagent.SendMessageAsync(agent.Namespace, agent.Name, sessionId, command.Message, command.UserId, ct));
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
