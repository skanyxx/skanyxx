using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Analytics;

namespace Skanyxx.Module.Analytics.Usage;

/// <summary>
/// Adds every chat turn to the rollup, after the answer exists. Bounded to <see cref="Budget"/> so a slow database adds at
/// most that to a chat, and any failure loses the count, never the chat: this handler is that boundary, so it logs
/// whatever goes wrong instead of throwing it into the chat's response.
/// </summary>
internal sealed class RecordChatUsageHandler(UsageStore store, ILogger<RecordChatUsageHandler> logger) : INotificationHandler<ChatTurnUsage>
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public async Task Handle(ChatTurnUsage turn, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Budget);
        try
        {
            await store.AddAsync(turn, deadline.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not add a chat turn of {UserId} to the usage rollup", turn.UserId);
        }
    }
}
