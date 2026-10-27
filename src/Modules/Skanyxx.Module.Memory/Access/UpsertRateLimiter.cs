using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Skanyxx.Module.Memory.Coordination;

namespace Skanyxx.Module.Memory.Access;

/// <summary>
/// A looping agent dies here, not in the table. Per caller and card (memory-writes.md §4: coalesce, don't write 50 times
/// a minute), then per caller (the secret's agent, or the signed-in user), then for everyone together, so many callers
/// at once still hit a ceiling. A caller over a narrower limit is refused before it spends a wider one. With Dragonfly
/// the counters are shared by every replica (one-minute windows); without it, or while it is down, they are this
/// process's (sliding windows), as day one (D043).
/// </summary>
public sealed class UpsertRateLimiter(IOptions<MemoryOptions> options, Dragonfly dragonfly) : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly MemoryOptions _options = options.Value;

    private readonly PartitionedRateLimiter<string> _perCard = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => PerMinute(options.Value.UpsertsPerKeyPerMinute)));

    private readonly PartitionedRateLimiter<string> _perCaller = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetSlidingWindowLimiter(key, _ => PerMinute(options.Value.UpsertsPerMinute)));

    private readonly SlidingWindowRateLimiter _total = new(PerMinute(options.Value.UpsertsPerMinuteTotal));

    /// <param name="card"><c>scope/key</c> for a write to one card; null for a write that is not an edit of one (a lift, a rename).</param>
    public async ValueTask<bool> TryAcquireAsync(string caller, string? card = null)
    {
        if (await dragonfly.DatabaseAsync() is { } redis)
        {
            try
            {
                return await SharedAsync(redis, caller, card);
            }
            catch (Exception ex) when (Dragonfly.IsFailure(ex))
            {
                dragonfly.Failed(ex);
            }
        }
        return Local(caller, card);
    }

    private bool Local(string caller, string? card)
    {
        if (card is not null)
        {
            using var perCard = _perCard.AttemptAcquire($"{caller}|{card}");
            if (!perCard.IsAcquired)
                return false;
        }
        using var perCaller = _perCaller.AttemptAcquire(caller);
        if (!perCaller.IsAcquired)
            return false;
        using var total = _total.AttemptAcquire();
        return total.IsAcquired;
    }

    private async Task<bool> SharedAsync(IDatabase redis, string caller, string? card)
    {
        var minute = DateTime.UtcNow.Ticks / Window.Ticks;
        return (card is null || await UnderAsync(redis, $"rl:card:{caller}|{card}:{minute}", _options.UpsertsPerKeyPerMinute))
               && await UnderAsync(redis, $"rl:caller:{caller}:{minute}", _options.UpsertsPerMinute)
               && await UnderAsync(redis, $"rl:total:{minute}", _options.UpsertsPerMinuteTotal);
    }

    private async Task<bool> UnderAsync(IDatabase redis, string name, int limit)
    {
        var key = dragonfly.Key(name);
        var count = await redis.StringIncrementAsync(key);
        if (count == 1)
            await redis.KeyExpireAsync(key, Window * 2);
        return count <= limit;
    }

    public void Dispose()
    {
        _perCard.Dispose();
        _perCaller.Dispose();
        _total.Dispose();
    }

    private static SlidingWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = Window,
        SegmentsPerWindow = 6,
        QueueLimit = 0
    };
}
