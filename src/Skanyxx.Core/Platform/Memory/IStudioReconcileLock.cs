namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// One studio reconcile at a time across every replica (D120): a Postgres session advisory lock in memory's database,
/// shared by the periodic pass and every merge-triggered or preview apply.
/// </summary>
public interface IStudioReconcileLock
{
    /// <summary>The held lock (dispose to release), or null when another holder kept it for all of <paramref name="wait"/>.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(TimeSpan wait, CancellationToken ct);
}
