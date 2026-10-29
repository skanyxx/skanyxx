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
        try
        {
            await foreach (var e in TurnAsync(ct))
                yield return e;
        }
        finally
        {
            // However the turn ended (also a cached answer, or a caller gone before kagent was asked).
            start.Admission.Dispose();
        }
    }

    private async IAsyncEnumerable<ChatStreamEvent> TurnAsync([EnumeratorCancellation] CancellationToken ct)
    {
        yield return ChatStreamEvent.Session(start.SessionId, start.IsNew, start.Title);
        if (start.Prepared.Cached is { } cached)
        {
            await turns.CachedAsync(command, start, cached);
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
                // Counted before anything is cleaned up: the cleanup may fail, the tokens were spent.
                await turns.RecordUsageAsync(command, start, turn.Usage, turn.Answer);
                await turns.AbandonAsync(command, start);
                await turns.FailedAsync(command, start);
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
            // Stopped, or the person left (also mid-write): counted first, then kagent stops working for nobody, and a
            // first turn leaves nothing.
            if (!finished)
            {
                await turns.RecordUsageAsync(command, start, turn.Usage, turn.Answer);
                if (turn.TaskId is { } task)
                    await turns.CancelKAgentAsync(command, start, task);
                await turns.AbandonAsync(command, start);
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
}
