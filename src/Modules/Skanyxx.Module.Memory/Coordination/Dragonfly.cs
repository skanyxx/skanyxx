using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Skanyxx.Module.Memory.Coordination;

/// <summary>
/// The optional Dragonfly connection (<see cref="DragonflyOptions"/>). Connects on first use and never blocks a request
/// on it: while it is down, commands fail fast and every caller falls back to what memory does without it (in-process
/// limits, no cache). Postgres stays the only store of cards (D042, D068).
/// </summary>
public sealed partial class Dragonfly(IOptions<MemoryOptions> options, ILogger<Dragonfly> logger) : IAsyncDisposable
{
    private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(1);

    private readonly DragonflyOptions _options = options.Value.Dragonfly;
    private readonly SemaphoreSlim _connecting = new(1, 1);
    private ConnectionMultiplexer? _connection;
    private long _lastWarningTicks;

    public bool Enabled => _options.Enabled;

    public string Key(string name) => _options.KeyPrefix + name;

    /// <summary>The database while Dragonfly is configured and connected; otherwise null (use the fallback).</summary>
    public async ValueTask<IDatabase?> DatabaseAsync()
    {
        if (!_options.Enabled)
            return null;
        try
        {
            var connection = _connection ?? await ConnectAsync();
            return connection.IsConnected ? connection.GetDatabase() : null;
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            Failed(ex);
            return null;
        }
    }

    /// <summary>A command failed: the caller falls back; this says so, at most once a minute.</summary>
    public void Failed(Exception ex)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (now - last >= WarnEvery.TotalMilliseconds && Interlocked.CompareExchange(ref _lastWarningTicks, now, last) == last)
            LogFallback(logger, ex);
    }

    public static bool IsFailure(Exception ex) => ex is RedisException or RedisTimeoutException or TimeoutException;

    private async Task<ConnectionMultiplexer> ConnectAsync()
    {
        await _connecting.WaitAsync();
        try
        {
            if (_connection is { } connected)
                return connected;
            var configuration = ConfigurationOptions.Parse(_options.Configuration!);
            // Never fail a request because Dragonfly is away: connect in the background, refuse commands while disconnected.
            configuration.AbortOnConnectFail = false;
            configuration.BacklogPolicy = BacklogPolicy.FailFast;
            configuration.ConnectTimeout = 2_000;
            configuration.SyncTimeout = 1_000;
            configuration.AsyncTimeout = 1_000;
            return _connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        }
        finally
        {
            _connecting.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connecting.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dragonfly did not answer; memory uses its in-process limits and no search cache until it does")]
    private static partial void LogFallback(ILogger logger, Exception ex);
}
