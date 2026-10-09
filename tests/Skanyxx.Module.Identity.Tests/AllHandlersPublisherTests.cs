using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Tests;

/// <summary>
/// D160: <see cref="AllHandlersPublisher"/> runs handlers in <see cref="NotificationOrderAttribute"/> order (registration
/// order between equals), and a cancelled publish stops before the next handler and surfaces as cancellation, not as an
/// aggregate of failures (CR L4) — unless a handler had already failed, which the cancellation must not hide (QA-2).
/// </summary>
public sealed class AllHandlersPublisherTests
{
    private sealed record Ping : INotification;

    [NotificationOrder(NotificationOrderAttribute.External)]
    private sealed class Slow;

    private sealed class Plain;

    [NotificationOrder(NotificationOrderAttribute.Revocation)]
    private sealed class Quick;

    [Fact]
    public async Task Handlers_RunInDeclaredOrder_EqualsInRegistrationOrder()
    {
        var ran = new List<string>();
        NotificationHandlerExecutor Handler(object instance, string name) => new(instance, (_, _) =>
        {
            ran.Add(name);
            return Task.CompletedTask;
        });

        await new AllHandlersPublisher().Publish(
            [Handler(new Slow(), "slow"), Handler(new Plain(), "plain-1"), Handler(new Quick(), "quick"), Handler(new Plain(), "plain-2")],
            new Ping(), CancellationToken.None);

        Assert.Equal(["quick", "plain-1", "plain-2", "slow"], ran);
    }

    [Fact]
    public async Task ACancelledPublish_StopsBeforeTheNextHandler()
    {
        using var abort = new CancellationTokenSource();
        var ran = new List<string>();
        NotificationHandlerExecutor[] handlers =
        [
            new(new Quick(), async (_, _) =>
            {
                ran.Add("first");
                await abort.CancelAsync(); // the client goes away while this handler finishes normally
            }),
            new(new Plain(), (_, _) =>
            {
                ran.Add("second");
                return Task.CompletedTask;
            })
        ];

        await Assert.ThrowsAsync<OperationCanceledException>(() => new AllHandlersPublisher().Publish(handlers, new Ping(), abort.Token));

        Assert.Equal(["first"], ran);
    }

    /// <summary>
    /// QA-2: a cancellation after another handler failed keeps the failure — both go out in an aggregate — and nothing
    /// after it runs.
    /// </summary>
    [Fact]
    public async Task ACancellation_AfterAFailure_KeepsTheFailure()
    {
        using var abort = new CancellationTokenSource();
        var ran = new List<string>();
        NotificationHandlerExecutor[] handlers =
        [
            new(new Quick(), (_, _) => throw new InvalidOperationException("memory is down")),
            new(new Plain(), async (_, ct) =>
            {
                await abort.CancelAsync();
                ct.ThrowIfCancellationRequested();
            }),
            new(new Slow(), (_, _) =>
            {
                ran.Add("third");
                return Task.CompletedTask;
            })
        ];

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => new AllHandlersPublisher().Publish(handlers, new Ping(), abort.Token));

        Assert.Collection(thrown.InnerExceptions,
            e => Assert.Equal("memory is down", Assert.IsType<InvalidOperationException>(e).Message),
            e => Assert.IsAssignableFrom<OperationCanceledException>(e));
        Assert.Empty(ran);
    }

    /// <summary>QA-2: the same when the cancellation is noticed before the next handler starts.</summary>
    [Fact]
    public async Task ACancellation_NoticedBetweenHandlers_AfterAFailure_KeepsTheFailure()
    {
        using var abort = new CancellationTokenSource();
        NotificationHandlerExecutor[] handlers =
        [
            new(new Quick(), async (_, _) =>
            {
                await abort.CancelAsync();
                throw new InvalidOperationException("memory is down");
            }),
            new(new Plain(), (_, _) => Task.CompletedTask)
        ];

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => new AllHandlersPublisher().Publish(handlers, new Ping(), abort.Token));

        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(thrown.InnerExceptions[0]);
    }
}
