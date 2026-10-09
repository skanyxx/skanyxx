using System.Reflection;
using System.Runtime.ExceptionServices;
using MediatR;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Runs every handler of a notification, one after the other in <see cref="NotificationOrderAttribute"/> order, even
/// when an earlier one throws, then rethrows: the single exception as it is, several as an <see cref="AggregateException"/>.
/// MediatR's default stops at the first failure, so an unreachable AX would have kept memory from revoking a disabled
/// person's agent secrets (D153). Cancellation is checked before each handler and an <see cref="OperationCanceledException"/>
/// it caused goes out as itself, so a client abort is not turned into an aggregate of failures (D160). After a failure
/// it goes out in an <see cref="AggregateException"/> with the failures, so a cancellation never hides one.
/// </summary>
public sealed class AllHandlersPublisher : INotificationPublisher
{
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        foreach (var handler in Ordered(handlerExecutors))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await handler.HandlerCallback(notification, cancellationToken);
            }
            catch (OperationCanceledException cancelled) when (cancellationToken.IsCancellationRequested)
            {
                if (failures is null)
                    throw;
                throw new AggregateException([.. failures, cancelled]);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is [var single])
            ExceptionDispatchInfo.Throw(single);
        if (failures is { Count: > 1 })
            throw new AggregateException(failures);
    }

    /// <summary>Stable: equal orders keep their registration order.</summary>
    public static IEnumerable<NotificationHandlerExecutor> Ordered(IEnumerable<NotificationHandlerExecutor> handlers) =>
        handlers.OrderBy(h => OrderOf(h.HandlerInstance.GetType()));

    public static int OrderOf(Type handler) => handler.GetCustomAttribute<NotificationOrderAttribute>()?.Order ?? 0;
}
