using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// A started turn, streamed from kagent's <c>message/stream</c>. A failure becomes an <c>error</c> event (and a first
/// turn's session is deleted, as for a blocking turn). Cancelling — the Stop button, or the person leaving — closes
/// kagent's stream, asks kagent to cancel the task, and deletes a first turn's session.
/// </summary>
internal sealed class ChatStream(SendChatMessageCommand command, TurnStart start, KAgentApiClient kagent, ChatTurns turns, ILogger<ChatStream> logger)
{
    public async IAsyncEnumerable<ChatStreamEvent> EventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        yield return ChatStreamEvent.Session(start.SessionId, start.IsNew, start.Title);
        if (start.Prepared.Cached is { } cached)
        {
            yield return ChatStreamEvent.Answer(cached.Message.Content);
            yield return ChatStreamEvent.Done(start.SessionId, true, ChatUsage.None, cached);
            yield break;
        }

        var turn = new A2ATurn();
        var finished = false;
        var results = kagent.StreamMessageAsync(start.Agent.Namespace, start.Agent.Name, start.SessionId, start.Prompt, start.Images, command.UserId, ct)
            .GetAsyncEnumerator(ct);
        try
        {
            string? failure = null;
            while (true)
            {
                var (more, error) = await NextAsync(results);
                failure = error;
                if (!more)
                    break;
                foreach (var e in turn.Read(results.Current))
                    yield return e;
            }

            failure ??= turn.Failure ?? (turn.Answer.Trim().Length == 0 ? ChatFailure.Empty : null);
            finished = true;
            if (failure is not null)
            {
                await turns.AbandonAsync(command, start);
                await turns.RecordUsageAsync(command, start, turn.Usage);
                yield return ChatStreamEvent.Error(failure, start.IsNew);
                yield break;
            }
            await turns.CompleteAsync(command, start, turn.Answer, turn.Usage);
            yield return ChatStreamEvent.Done(start.SessionId, false, turn.Usage,
                new ChatResponse { RelatedSessions = start.Prepared.Sessions, MemoryCards = start.Memory.Cards });
        }
        finally
        {
            await results.DisposeAsync();
            // Stopped, or the person left (also mid-write): kagent stops working for nobody, a first turn leaves nothing.
            if (!finished)
            {
                if (turn.TaskId is { } task)
                    await CancelTaskAsync(task);
                await turns.AbandonAsync(command, start);
                await turns.RecordUsageAsync(command, start, turn.Usage);
            }
        }
    }

    // An iterator cannot catch around a yield, so each read is caught here.
    private async Task<(bool More, string? Error)> NextAsync(IAsyncEnumerator<JsonNode> results)
    {
        try
        {
            return (await results.MoveNextAsync(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Streamed chat with {AgentRef} failed for {UserId}", start.AgentRef, command.UserId);
            return (false, ChatFailure.Message(ex));
        }
    }

    // Not the request's token (it is cancelled): kagent should stop working for nobody.
    private async Task CancelTaskAsync(string taskId)
    {
        try
        {
            await kagent.CancelTaskAsync(start.Agent.Namespace, start.Agent.Name, taskId, command.UserId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogInformation(ex, "kagent did not cancel task {TaskId} (it may have finished)", taskId);
        }
    }
}
