using Microsoft.Extensions.Logging;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// One turn with a studio agent that is not in Chat (a preview, the factory), as the signed-in person, the way Chat talks
/// to merged agents (D101): kagent sessions are per user, a continued conversation must be the caller's and this
/// agent's, and a failed first turn deletes the session it created. The token quota admits the turn and counts it once
/// whatever ended it, a malformed answer included (D383, D388). When our deadline passes or the caller leaves, kagent is
/// asked to stop this turn's task, as Chat does (D381).
/// </summary>
internal sealed class StudioChat(KAgentApiClient kagent, IChatQuota quota, ITokenEstimator estimator, ILogger<StudioChat> logger)
{
    /// <param name="model">The agent's ModelConfig as <c>namespace/name</c> (the quota's price key).</param>
    public async Task<Outcome<ChatResponse>> SendAsync(
        string ns, string agent, string model, string userId, string message, string? conversationId, CancellationToken ct)
    {
        var agentRef = $"{ns}/{agent}";
        using var admission = await quota.AdmitAsync(userId, agentRef, ct);
        if (admission.Refusal is { } refusal)
            return Outcome<ChatResponse>.RateLimited(refusal);
        var messageId = Guid.NewGuid().ToString();
        string? created = null, sent = null;
        ChatResponse? response = null;
        var recorded = false;
        async Task RecordOnceAsync()
        {
            if (sent is null || recorded)
                return;
            recorded = true;
            await RecordAsync(userId, agentRef, model, sent, response is null ? ChatUsage.None : ChatUsage.Of(response.Usage), message, response?.Message.Content ?? "");
        }

        try
        {
            string sessionId;
            if (string.IsNullOrEmpty(conversationId))
                sessionId = created = (await kagent.CreateSessionAsync(agentRef, userId, ct)).Id;
            else if (await kagent.GetSessionAsync(conversationId, userId, ct) is { } session
                     && session.AgentId == agentRef.Replace("-", "_").Replace("/", "__NS__"))
                sessionId = session.Id;
            else
                return Outcome<ChatResponse>.NotFound("No such conversation.");
            sent = sessionId;
            response = await kagent.SendMessageAsync(ns, agent, sessionId, message, userId, ct, messageId: messageId);
            await RecordOnceAsync();
            return Outcome<ChatResponse>.Ok(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio chat with {Namespace}/{Agent} failed for {UserId}", ns, agent, userId);
            await RecordOnceAsync();
            // Our deadline passed, not kagent's: it may still be working.
            if (ex is TimeoutException && sent is not null)
                await kagent.CancelBlockingTurnAsync(ns, agent, sent, messageId, userId);
            if (created is not null)
                await DeleteAsync(created, userId);
            return Outcome<ChatResponse>.Unavailable(ex is TimeoutException
                ? "The agent did not answer in time. Try again, or ask something shorter."
                : "The agent cannot answer right now (a new agent takes a minute to start). Try again in a moment.");
        }
        catch (OperationCanceledException) when (sent is not null && ct.IsCancellationRequested)
        {
            await RecordOnceAsync();
            await kagent.CancelBlockingTurnAsync(ns, agent, sent, messageId, userId);
            if (created is not null)
                await DeleteAsync(created, userId);
            throw;
        }
        finally
        {
            await RecordOnceAsync();
        }
    }

    // Not the request's token: the tokens were spent whether or not the caller is still there.
    private Task RecordAsync(string userId, string agentRef, string model, string sessionId, ChatUsage usage, string prompt, string answer) =>
        quota.RecordAsync(ChatQuotaUsage.Measure(estimator, userId, agentRef, model, sessionId, usage, prompt, answer), CancellationToken.None);

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
