using Microsoft.Extensions.Logging;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// One turn with a studio agent that is not in Chat (a preview, the factory), as the signed-in person, the way Chat talks
/// to merged agents (D101): kagent sessions are per user, a continued conversation must be the caller's and this
/// agent's, and a failed first turn deletes the session it created.
/// </summary>
internal sealed class StudioChat(KAgentApiClient kagent, ILogger<StudioChat> logger)
{
    public async Task<Outcome<ChatResponse>> SendAsync(string ns, string agent, string userId, string message, string? conversationId, CancellationToken ct)
    {
        string? created = null;
        try
        {
            string sessionId;
            if (string.IsNullOrEmpty(conversationId))
                sessionId = created = (await kagent.CreateSessionAsync($"{ns}/{agent}", userId, ct)).Id;
            else if (await kagent.GetSessionAsync(conversationId, userId, ct) is { } session
                     && session.AgentId == $"{ns}/{agent}".Replace("-", "_").Replace("/", "__NS__"))
                sessionId = session.Id;
            else
                return Outcome<ChatResponse>.NotFound("No such conversation.");
            return Outcome<ChatResponse>.Ok(await kagent.SendMessageAsync(ns, agent, sessionId, message, userId, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio chat with {Namespace}/{Agent} failed for {UserId}", ns, agent, userId);
            if (created is not null)
                await DeleteAsync(created, userId);
            return Outcome<ChatResponse>.Unavailable(ex is TimeoutException
                ? "The agent did not answer in time. Try again, or ask something shorter."
                : "The agent cannot answer right now (a new agent takes a minute to start). Try again in a moment.");
        }
    }

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
