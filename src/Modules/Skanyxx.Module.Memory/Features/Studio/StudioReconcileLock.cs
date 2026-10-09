using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// D120: a session advisory lock on its own unpooled connection (a pooled one keeps its session, and so the lock, after
/// Dispose). The lock lives exactly as long as the returned handle's connection; a replica that dies, or whose lock
/// connection drops mid-pass, loses it (and nothing notices until the release). Needs a session-mode connection: a
/// transaction-mode pooler (pgbouncer) in front of Postgres would break it.
/// </summary>
internal sealed class StudioReconcileLock(IOptions<MemoryOptions> options, ILogger<StudioReconcileLock> logger) : IStudioReconcileLock
{
    /// <summary>Listed with every other key in <see cref="Data.MemoryMigrator"/>.</summary>
    internal const long LockKey = 0x4D454D03;

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    public async Task<IAsyncDisposable?> TryAcquireAsync(TimeSpan wait, CancellationToken ct)
    {
        var settings = options.Value.ConnectionSettings();
        settings.Pooling = false;
        settings.KeepAlive = 30;
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            var deadline = DateTime.UtcNow + wait;
            while (true)
            {
                await using (var command = new NpgsqlCommand($"SELECT pg_try_advisory_lock({LockKey})", connection))
                    if ((bool)(await command.ExecuteScalarAsync(ct))!)
                        return new Held(connection, logger);
                if (DateTime.UtcNow >= deadline)
                    break;
                await Task.Delay(Poll, ct);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        await connection.DisposeAsync();
        return null;
    }

    private sealed class Held(NpgsqlConnection connection, ILogger logger) : IAsyncDisposable
    {
        // Closing the session releases the lock; the explicit unlock is for a server that keeps the session a moment.
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var unlock = new NpgsqlCommand($"SELECT pg_advisory_unlock({LockKey})", connection);
                await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The connection is gone (Npgsql throws more than NpgsqlException for that), and the lock with it. Never
                // thrown: it would replace the pass's own exception or result (round 2 m2).
                logger.LogWarning(ex, "Studio reconcile lock: the unlock failed; the lock went with its connection");
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
