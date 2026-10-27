namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>The few Redis commands the answer cache needs; Dragonfly speaks the same protocol.</summary>
internal interface IAnswerStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value, TimeSpan ttl);

    Task<long> IncrementAsync(string key, long by = 1);

    /// <summary>0 for a missing counter.</summary>
    Task<long> CounterAsync(string key);
}
