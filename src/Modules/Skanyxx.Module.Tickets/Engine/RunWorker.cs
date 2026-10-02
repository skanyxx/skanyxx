using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Moves every runnable run forward one stage per sweep, round-robin, so one long pipeline cannot starve the
/// rest. State lives in Postgres, so runs in flight when the host stopped are picked up again on start.
/// Only the replica holding a session advisory lock works, so a rolling update never runs two workers (two
/// workers would pay for every stage twice). The lock is re-checked before every step, so on failover at most the
/// one step in flight is paid twice.
/// A run whose step keeps throwing is failed with the reason after <see cref="Attempts"/> tries rather than
/// retried forever (every retry is a paid model turn); a single blip does not end it.
/// </summary>
internal sealed partial class RunWorker(
    IServiceScopeFactory scopes, RunSignal signal, IOptions<TicketsOptions> options, ILogger<RunWorker> logger)
    : BackgroundService
{
    private const int Attempts = 3;
    internal const long LeaderLockKey = 0x544B5402;

    // Consecutive failed steps per run. Only this worker touches it, from one loop.
    private readonly Dictionary<Guid, int> _failures = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(options.Value.PollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var leader = await LeadAsync(poll, stoppingToken);
                // Counts from an earlier term are stale: another replica may have stepped those runs since.
                _failures.Clear();
                await WorkAsync(leader, poll, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The database went away (or the lock with it): step down, then take the lock again.
                LogSteppedDown(logger, ex);
                await Task.Delay(poll, stoppingToken);
            }
        }
    }

    /// <summary>Waits until this replica holds the worker lock; the lock lives as long as the returned connection.</summary>
    private async Task<NpgsqlConnection> LeadAsync(TimeSpan poll, CancellationToken ct)
    {
        while (true)
        {
            // Unpooled: a pooled connection keeps its session, and so the lock, after Dispose.
            // KeepAlive: the connection idles through each (long) agent call; without it a dead peer holds the lock
            // until the OS notices (hours), and an idle-timeout proxy would cut it mid-step.
            var unpooled = options.Value.ConnectionSettings();
            unpooled.Pooling = false;
            unpooled.KeepAlive = 30;
            var connection = new NpgsqlConnection(unpooled.ConnectionString);
            var leading = false;
            try
            {
                await connection.OpenAsync(ct);
                await using var command = new NpgsqlCommand($"SELECT pg_try_advisory_lock({LeaderLockKey})", connection);
                leading = (bool)(await command.ExecuteScalarAsync(ct))!;
            }
            finally
            {
                if (!leading)
                    await connection.DisposeAsync();
            }

            if (leading)
                return connection;
            await Task.Delay(poll, ct);
        }
    }

    private async Task WorkAsync(NpgsqlConnection leader, TimeSpan poll, CancellationToken stoppingToken)
    {
        await using var alive = new NpgsqlCommand("SELECT 1", leader);
        while (!stoppingToken.IsCancellationRequested)
        {
            List<Guid> runnable = [];
            try
            {
                runnable = await RunnableAsync(stoppingToken);
                foreach (var gone in _failures.Keys.Except(runnable).ToList())
                    _failures.Remove(gone);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Listing runs failed (the database is unreachable): try again on the next poll.
                LogSweepFailed(logger, ex);
            }

            var moved = false;
            foreach (var id in runnable)
            {
                // Per step, not per sweep: a sweep can outlive the lock by many stage timeouts. Throws if the
                // lock's connection is gone, and the caller steps down.
                await alive.ExecuteScalarAsync(stoppingToken);
                moved |= await StepAsync(id, stoppingToken);
            }

            if (!moved)
                await signal.WaitAsync(poll, stoppingToken);
        }
    }

    private async Task<List<Guid>> RunnableAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TicketsDbContext>().Runs
            .Where(r => r.State == RunState.Pending || r.State == RunState.Running)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync(ct);
    }

    private async Task<bool> StepAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var moved = await scope.ServiceProvider.GetRequiredService<RunStepper>().StepAsync(id, ct);
            _failures.Remove(id);
            return moved;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogStepFailed(logger, id, ex);
            var failures = _failures[id] = _failures.GetValueOrDefault(id) + 1;
            if (failures >= Attempts)
                await FailAsync(id, $"the run stopped on an internal error ({ex.GetType().Name}) after {failures} tries", ct);
            return false;
        }
    }

    private async Task FailAsync(Guid id, string reason, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RunStepper>().FailAsync(id, reason, ct);
            _failures.Remove(id);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Not even the failure could be saved (e.g. the database is down): the next sweep tries again.
            LogStepFailed(logger, id, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The ticket worker lost the database or its lock; stepping down")]
    private static partial void LogSteppedDown(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Listing runnable ticket runs failed; retrying on the next poll")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ticket run {RunId} failed on an internal error")]
    private static partial void LogStepFailed(ILogger logger, Guid runId, Exception ex);
}
