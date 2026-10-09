using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class KeyedLockTests
{
    [Fact]
    public async Task SameKey_Serialises_OtherKeys_DoNot()
    {
        var locks = new KeyedLock();
        var hold = new TaskCompletionSource<Outcome<int>>();

        var first = locks.RunAsync("a", () => hold.Task, CancellationToken.None);
        var sameKey = locks.RunAsync("a", () => Task.FromResult(Outcome<int>.Ok(2)), CancellationToken.None);
        var otherKey = await locks.RunAsync("b", () => Task.FromResult(Outcome<int>.Ok(3)), CancellationToken.None);

        Assert.Equal(3, otherKey.Value);
        Assert.False(sameKey.IsCompleted);
        hold.SetResult(Outcome<int>.Ok(1));
        Assert.Equal(2, (await sameKey).Value);
        Assert.Equal(1, (await first).Value);
    }

    [Fact]
    public async Task AKey_AdmitsAtMostMaxPerKey_ThenRefuses_AndIsRemovedWhenIdle()
    {
        var locks = new KeyedLock();
        var hold = new TaskCompletionSource<Outcome<int>>();

        var queued = Enumerable.Range(0, KeyedLock.MaxPerKey).Select(_ => locks.RunAsync("a", () => hold.Task, CancellationToken.None)).ToList();
        var refused = await locks.RunAsync("a", () => hold.Task, CancellationToken.None);

        Assert.Equal(OutcomeStatus.RateLimited, refused.Status);
        hold.SetResult(Outcome<int>.Ok(1));
        await Task.WhenAll(queued);
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task ACancelledWaiter_LeavesTheKey()
    {
        var locks = new KeyedLock();
        var hold = new TaskCompletionSource<Outcome<int>>();
        using var cancel = new CancellationTokenSource();

        var holder = locks.RunAsync("a", () => hold.Task, CancellationToken.None);
        var waiter = locks.RunAsync("a", () => hold.Task, cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        hold.SetResult(Outcome<int>.Ok(1));
        await holder;
        Assert.Equal(0, locks.Count);
    }

    [Fact]
    public async Task AThrowingBody_ReleasesAndRemovesTheKey()
    {
        var locks = new KeyedLock();
        var hold = new TaskCompletionSource<Outcome<int>>();

        var thrower = locks.RunAsync("a", () => hold.Task, CancellationToken.None);
        var waiter = locks.RunAsync("a", () => Task.FromResult(Outcome<int>.Ok(2)), CancellationToken.None);
        hold.SetException(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => thrower);
        Assert.Equal(2, (await waiter.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Value);
        Assert.Equal(0, locks.Count);
    }
}
