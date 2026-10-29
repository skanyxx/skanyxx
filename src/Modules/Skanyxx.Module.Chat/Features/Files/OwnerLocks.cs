namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>
/// Runs one person's file writes one at a time. A person's <see cref="SemaphoreSlim"/> exists only while someone holds
/// or waits for it, so the map is as large as the writes in flight, never the number of people.
/// </summary>
internal sealed class OwnerLocks
{
    private readonly Dictionary<string, Entry> _entries = [];

    /// <summary>People with a write in flight or waiting.</summary>
    public int Count
    {
        get
        {
            lock (_entries)
                return _entries.Count;
        }
    }

    public async Task<T> RunAsync<T>(string owner, Func<Task<T>> work, CancellationToken ct)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(owner, out entry!))
                _entries[owner] = entry = new Entry();
            entry.Users++;
        }
        try
        {
            await entry.Semaphore.WaitAsync(ct);
            try
            {
                return await work();
            }
            finally
            {
                entry.Semaphore.Release();
            }
        }
        finally
        {
            lock (_entries)
                if (--entry.Users == 0)
                {
                    _entries.Remove(owner);
                    entry.Semaphore.Dispose();
                }
        }
    }

    public Task RunAsync(string owner, Func<Task> work, CancellationToken ct) =>
        RunAsync(owner, async () =>
        {
            await work();
            return true;
        }, ct);

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }
    }
}
