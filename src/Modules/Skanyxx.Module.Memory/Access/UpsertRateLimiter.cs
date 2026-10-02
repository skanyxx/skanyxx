using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Memory.Access;

/// <summary>
/// A looping agent dies here, not in the table. Per caller (the secret's agent, or the signed-in user), then for
/// everyone together, so many callers at once still hit a ceiling. In-process: one memory replica day one (D043).
/// </summary>
public sealed class UpsertRateLimiter(IOptions<MemoryOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> _perCaller = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => PerMinute(options.Value.UpsertsPerMinute)));

    private readonly SlidingWindowRateLimiter _total = new(PerMinute(options.Value.UpsertsPerMinuteTotal));

    /// <summary>A caller over its own limit is refused before it can spend the shared budget.</summary>
    public bool TryAcquire(string key)
    {
        using var caller = _perCaller.AttemptAcquire(key);
        if (!caller.IsAcquired)
            return false;
        using var total = _total.AttemptAcquire();
        return total.IsAcquired;
    }

    public void Dispose()
    {
        _perCaller.Dispose();
        _total.Dispose();
    }

    private static SlidingWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0
    };
}
