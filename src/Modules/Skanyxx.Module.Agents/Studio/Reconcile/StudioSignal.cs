namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>Wakes the reconcile loop before its next tick (setup made the owner, an agent was resumed).</summary>
internal sealed class StudioSignal : IDisposable
{
    private readonly SemaphoreSlim _poked = new(0, 1);

    public void Poke()
    {
        try
        {
            _poked.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already poked: one pass covers both.
        }
    }

    /// <returns>True when poked, false when <paramref name="interval"/> passed.</returns>
    public Task<bool> WaitAsync(TimeSpan interval, CancellationToken ct) => _poked.WaitAsync(interval, ct);

    public void Dispose() => _poked.Dispose();
}
