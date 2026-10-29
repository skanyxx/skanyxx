using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Engine;

/// <summary>
/// Takes queued runs, oldest first, and executes up to <see cref="InvestigateOptions.MaxConcurrentRuns"/> at once. A run is
/// claimed by one conditional update (queued → running), so with several replicas each run executes once, and starts as
/// soon as it is claimed, so an error claiming the next one never strands it. Running runs whose heartbeat stopped
/// (their replica died) are failed with a reason; heartbeats and that sweep both read the database's clock, never a
/// replica's (D373). On shutdown every executing run is cancelled and awaited, and ends as failed ("Skanyxx stopped"),
/// never left running.
/// </summary>
internal sealed partial class InvestigationWorker(
    IServiceScopeFactory scopes,
    IDbContextFactory<InvestigateDbContext> dbs,
    RunSignal signal,
    IOptions<InvestigateOptions> options,
    ILogger<InvestigationWorker> logger) : BackgroundService
{
    private const int ClaimCandidates = 5;

    private readonly InvestigateOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(_options.PollSeconds);
        var executing = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                executing.RemoveAll(t => t.IsCompleted);
                try
                {
                    await FailStaleAsync(stoppingToken);
                    while (executing.Count < _options.MaxConcurrentRuns && await ClaimOneAsync(stoppingToken) is { } id)
                        executing.Add(Task.Run(() => ExecuteAsync(id, stoppingToken), CancellationToken.None));
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    LogSweepFailed(logger, ex);
                }

                await signal.WaitAsync(poll, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
        finally
        {
            await Task.WhenAll(executing);
        }
    }

    private async Task ExecuteAsync(Guid id, CancellationToken stoppingToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<RunExecutor>().ExecuteAsync(id, stoppingToken);
        // A slot is free: look at the queue now, not at the next poll.
        signal.Notify();
    }

    /// <returns>The oldest queued run, now running on this replica; null when the queue is empty.</returns>
    private async Task<Guid?> ClaimOneAsync(CancellationToken ct)
    {
        await using var db = await dbs.CreateDbContextAsync(ct);
        // A few candidates: another replica may take the oldest between the read and the update.
        foreach (var id in await db.Runs.Where(r => r.State == RunState.Queued).OrderBy(r => r.CreatedAt).Select(r => r.Id).Take(ClaimCandidates).ToListAsync(ct))
            if (await db.Runs.Where(r => r.Id == id && r.State == RunState.Queued)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.State, RunState.Running)
                        .SetProperty(r => r.StartedAt, r => DateTime.UtcNow)
                        .SetProperty(r => r.HeartbeatAt, r => DateTime.UtcNow), ct) == 1)
                return id;
        return null;
    }

    /// <summary>
    /// Fails runs whose heartbeat is older than <see cref="InvestigateOptions.StaleAfterSeconds"/> by the database's clock.
    /// The run is ended first, by a conditional update, so a run that stamps a heartbeat meanwhile is left alone and its
    /// steps untouched; then, in the same transaction, its steps end as <see cref="RunExecutor"/> ends them: never started
    /// skipped, running cancelled.
    /// </summary>
    private async Task FailStaleAsync(CancellationToken ct)
    {
        var staleAfter = _options.StaleAfterSeconds;
        await using var db = await dbs.CreateDbContextAsync(ct);
        var ids = await db.Runs.Where(r => r.State == RunState.Running && r.HeartbeatAt < DateTime.UtcNow.AddSeconds(-staleAfter)).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            // One transaction: nobody reads a run that ended with steps still running.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await db.Runs.Where(r => r.Id == id && r.State == RunState.Running && r.HeartbeatAt < DateTime.UtcNow.AddSeconds(-staleAfter))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.State, RunState.Failed)
                        .SetProperty(r => r.Failure, "The server running this investigation stopped; run it again.")
                        .SetProperty(r => r.EndedAt, r => DateTime.UtcNow), ct) == 0)
                continue;
            await db.StepRuns.Where(s => s.RunId == id && s.State == StepState.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Skipped), ct);
            await db.StepRuns.Where(s => s.RunId == id && s.State == StepState.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Cancelled).SetProperty(x => x.EndedAt, x => DateTime.UtcNow), ct);
            await tx.CommitAsync(ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Looking for queued investigation runs failed; retrying on the next poll")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);
}
