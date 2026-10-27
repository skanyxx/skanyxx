using System.Collections.Concurrent;
using Skanyxx.Module.ChatContext.Cache;
using StackExchange.Redis;

namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

/// <summary>Redis's semantics for the four commands, in memory; <see cref="Down"/> fails every call as an unreachable Redis does.</summary>
internal sealed class FakeAnswerStore : IAnswerStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public bool Down { get; set; }

    /// <summary>Every call times out, as a Redis that accepts connections but does not answer: a TimeoutException, not a RedisException.</summary>
    public bool TimingOut { get; set; }

    public int Reads { get; private set; }

    public IReadOnlyDictionary<string, string> Values => _values;

    public Task<string?> GetAsync(string key)
    {
        Check();
        Reads++;
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value, TimeSpan ttl)
    {
        Check();
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<long> IncrementAsync(string key, long by = 1)
    {
        Check();
        return Task.FromResult(long.Parse(_values.AddOrUpdate(key, by.ToString(), (_, v) => (long.Parse(v) + by).ToString())));
    }

    public Task<long> CounterAsync(string key)
    {
        Check();
        return Task.FromResult(_values.TryGetValue(key, out var value) ? long.Parse(value) : 0);
    }

    private void Check()
    {
        if (Down)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test: redis is down");
        if (TimingOut)
            throw new RedisTimeoutException("test: redis timed out", CommandStatus.Sent);
    }
}
