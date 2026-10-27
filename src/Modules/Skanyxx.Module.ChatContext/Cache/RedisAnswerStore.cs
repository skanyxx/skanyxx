using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>
/// StackExchange.Redis against <see cref="AnswerCacheOptions.Redis"/>. Connects on first use and keeps reconnecting in the
/// background (<c>abortConnect=false</c>), so a cache that is down at startup does not stop the Host.
/// </summary>
internal sealed class RedisAnswerStore(IOptions<ChatContextOptions> options) : IAnswerStore, IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection = new(() =>
    {
        var config = ConfigurationOptions.Parse(options.Value.AnswerCache.Redis);
        config.AbortOnConnectFail = false;
        return ConnectionMultiplexer.ConnectAsync(config);
    });

    public async Task<string?> GetAsync(string key) => await (await DatabaseAsync()).StringGetAsync(key);

    public async Task SetAsync(string key, string value, TimeSpan ttl) => await (await DatabaseAsync()).StringSetAsync(key, value, ttl);

    public async Task<long> IncrementAsync(string key, long by = 1) => await (await DatabaseAsync()).StringIncrementAsync(key, by);

    public async Task<long> CounterAsync(string key) => (long?)await (await DatabaseAsync()).StringGetAsync(key) ?? 0;

    private async Task<IDatabase> DatabaseAsync() => (await _connection.Value).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
            await (await _connection.Value).DisposeAsync();
    }
}
