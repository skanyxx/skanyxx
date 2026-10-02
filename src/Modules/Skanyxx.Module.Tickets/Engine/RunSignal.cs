namespace Skanyxx.Module.Tickets.Engine;

/// <summary>Wakes the worker when a run becomes runnable, instead of waiting for the next poll.</summary>
public sealed class RunSignal : IDisposable
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Notify()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled; one wake-up covers every run.
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _wake.WaitAsync(timeout, ct);

    public void Dispose() => _wake.Dispose();
}
