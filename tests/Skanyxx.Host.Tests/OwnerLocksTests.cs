using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Host.Tests;

/// <summary>D326: one person's file writes run one at a time, other people's do not wait, and no entry outlives its writes.</summary>
public sealed class OwnerLocksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnePersonsWrites_NeverOverlap_AndTheMapEmptiesWhenTheyAreDone()
    {
        var locks = new OwnerLocks();
        var inside = 0;
        var most = 0;

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => locks.RunAsync("ann", async () =>
        {
            most = Math.Max(most, Interlocked.Increment(ref inside));
            await Task.Delay(1, Ct);
            Interlocked.Decrement(ref inside);
        }, Ct)));

        Assert.Equal(1, most);
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task AnotherPerson_DoesNotWait()
    {
        var locks = new OwnerLocks();
        var release = new TaskCompletionSource();
        var held = locks.RunAsync("ann", () => release.Task, Ct);

        await locks.RunAsync("bob", () => Task.CompletedTask, Ct);

        Assert.Equal(1, locks.Count);
        release.SetResult();
        await held;
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task ACancelledWaiter_OrAFailingWrite_LeavesNoEntry()
    {
        var locks = new OwnerLocks();
        var release = new TaskCompletionSource();
        var held = locks.RunAsync("ann", () => release.Task, Ct);
        using var cancel = new CancellationTokenSource();
        var waiting = locks.RunAsync("ann", () => Task.CompletedTask, cancel.Token);

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        await held;
        await Assert.ThrowsAsync<IOException>(() => locks.RunAsync("ann", async () =>
        {
            await Task.Yield();
            throw new IOException("disk full");
        }, Ct));

        Assert.Equal(0, locks.Count);
    }
}
