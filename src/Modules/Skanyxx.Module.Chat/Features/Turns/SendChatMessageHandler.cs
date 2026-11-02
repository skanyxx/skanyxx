using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// One blocking turn (<c>POST api/chat</c>); the rules are <see cref="ChatTurns"/>'. A turn that fails, runs out of time
/// or that the caller leaves still counts (at least its prompt), and in the last two cases kagent is asked to stop it,
/// as a stopped stream is (D380, D381). A task kagent ended badly answers as a streamed one does: fixed words, and
/// kagent's reason for builders and up (D553).
/// </summary>
internal sealed class SendChatMessageHandler(KAgentApiClient kagent, ChatTurns turns, ILogger<SendChatMessageHandler> logger)
    : IRequestHandler<SendChatMessageCommand, Outcome<ChatResponse>>
{
    public async Task<Outcome<ChatResponse>> Handle(SendChatMessageCommand command, CancellationToken ct)
    {
        TurnStart? start = null;
        try
        {
            var started = await turns.StartAsync(command, ct);
            if (started.Value is null)
                return started.Map<ChatResponse>(_ => null!);
            start = started.Value;
            if (start.Prepared.Cached is { } cached)
            {
                await turns.CachedAsync(command, start, cached);
                return Outcome<ChatResponse>.Ok(cached);
            }

            var response = await kagent.SendMessageAsync(start.Agent.Namespace, start.Agent.Name, start.SessionId, start.Prompt, command.UserId, ct, start.Images, start.MessageId);
            response.RelatedSessions = start.Prepared.Sessions;
            response.MemoryCards = start.Memory.Cards;
            response.ClientTokens = start.ClientTokens;
            await turns.CompleteAsync(command, start, response.Message.Content, ChatUsage.None, response.Usage);
            return Outcome<ChatResponse>.Ok(response);
        }
        catch (KAgentTaskFailedException ex) when (start is not null)
        {
            // kagent answered: the task ended badly. KAgentApiClient logged its reason with the task; this names the person.
            logger.LogWarning("Chat with {AgentRef} failed for {UserId}: task {TaskId} ended {State}", start.AgentRef, command.UserId, ex.TaskId, ex.State);
            await turns.RecordUsageAsync(command, start, ChatUsage.Of(ex.Usage), "");
            await turns.AbandonAsync(command, start);
            await turns.FailedAsync(command, start);
            // As the stream answers it: an approval stop is not a failure, and its words are not kagent's error.
            return ex.State is "input-required" or "auth-required"
                ? Outcome<ChatResponse>.Unavailable(ChatFailure.NeedsApproval)
                : Outcome<ChatResponse>.Unavailable(ChatFailure.Unfinished, command.SeesFailureReason ? ex.Reason : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Chat with {Namespace}/{Agent} failed for {UserId}", command.AgentNamespace, command.AgentName, command.UserId);
            if (start is not null)
            {
                await turns.RecordUsageAsync(command, start, ChatUsage.None, "");
                // Our deadline passed, not kagent's: it may still be working.
                if (ex is TimeoutException)
                    await turns.CancelKAgentAsync(command, start, null);
                await turns.AbandonAsync(command, start);
                await turns.FailedAsync(command, start);
            }
            return Outcome<ChatResponse>.Unavailable(ChatFailure.Message(ex));
        }
        catch (OperationCanceledException) when (start is not null && ct.IsCancellationRequested)
        {
            // The caller left (a closed tab): the prompt was spent, kagent stops working for nobody, and nobody will
            // continue a first turn's conversation.
            await turns.RecordUsageAsync(command, start, ChatUsage.None, "");
            await turns.CancelKAgentAsync(command, start, null);
            await turns.AbandonAsync(command, start);
            throw;
        }
        finally
        {
            start?.Admission.Dispose();
        }
    }
}
