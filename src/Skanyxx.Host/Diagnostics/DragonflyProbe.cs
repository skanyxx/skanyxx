using System.Diagnostics;
using Skanyxx.Core.Platform;
using StackExchange.Redis;

namespace Skanyxx.Host.Diagnostics;

/// <summary>
/// Memory's Dragonfly (<c>Memory:Dragonfly:Configuration</c>) has no health check of its own, so the Debug page
/// connects once and PINGs, bounded by <see cref="Timeout"/>. The error is shown redacted (a configuration string can
/// carry a password).
/// </summary>
public static class DragonflyProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<Connectivity> CheckAsync(string name, string? configuration, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            return new(name, "not configured", null, null);
        var watch = Stopwatch.StartNew();
        try
        {
            var options = ConfigurationOptions.Parse(configuration);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = (int)Timeout.TotalMilliseconds;
            await using var connection = await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(Timeout, ct);
            await connection.GetDatabase().PingAsync().WaitAsync(Timeout, ct);
            return new(name, "Healthy", "PING answered", (int)watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new(name, "Unhealthy", $"{ex.GetType().Name}: {SecretText.Redact(ex.Message)}", (int)watch.ElapsedMilliseconds);
        }
    }
}
