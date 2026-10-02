using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>
/// One <see cref="SemaphoreSlim"/> per key, alive only while someone holds or waits for it. Run, stop, suspend and
/// resume hold the task's key (so a run cannot undo a suspend it raced); a run that activates a task then also holds
/// <see cref="Atespace"/> for count → upsert, so parallel runs cannot all pass the caps. Always task before atespace,
/// and nothing takes a second task key: no cycles. Within one replica only; AX has no conditional write, so across
/// replicas this is best-effort.
/// </summary>
internal sealed class KeyedLock
{
    /// <summary>Holders plus waiters per key; beyond it a request is refused rather than queued.</summary>
    public const int MaxPerKey = 16;

    private readonly Dictionary<string, (SemaphoreSlim Gate, int Users)> _gates = [];

    public const string Atespace = "atespace";

    public static string ForTask(string name) => "task:" + name;

    /// <summary>Keys currently held or waited for.</summary>
    public int Count
    {
        get
        {
            lock (_gates)
                return _gates.Count;
        }
    }

    public async Task<Outcome<T>> RunAsync<T>(string key, Func<Task<Outcome<T>>> body, CancellationToken ct)
    {
        var gate = Join(key);
        if (gate is null)
            return Outcome<T>.RateLimited("Too many requests for this task, or task starts, are in flight; try again shortly.");
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                return await body();
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            Leave(key);
        }
    }

    private SemaphoreSlim? Join(string key)
    {
        lock (_gates)
        {
            var (gate, users) = _gates.TryGetValue(key, out var entry) ? entry : (new SemaphoreSlim(1, 1), 0);
            if (users == MaxPerKey)
                return null;
            _gates[key] = (gate, users + 1);
            return gate;
        }
    }

    private void Leave(string key)
    {
        lock (_gates)
        {
            var (gate, users) = _gates[key];
            if (users > 1)
            {
                _gates[key] = (gate, users - 1);
                return;
            }
            _gates.Remove(key);
            gate.Dispose();
        }
    }
}
