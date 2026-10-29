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
/// claimed by one conditional update (queued → running), so with several replicas each run executes once. Running runs
/// whose heartbeat stopped (their replica died) are failed with a reason. On shutdown every executing run is cancelled
/// and awaited, and ends as failed ("Skanyxx stopped"), never left running.
/// </summary>
internal sealed partial class InvestigationWorker(
    IServiceScopeFactory scopes,
    IDbContextFactory<InvestigateDbContext> dbs,
    RunSignal signal,
    IOptions<InvestigateOptions> options,
    TimeProvider clock,
    ILogger<InvestigationWorker> logger) : BackgroundService
{
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
                    foreach (var id in await ClaimAsync(_options.MaxConcurrentRuns - executing.Count, stoppingToken))
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

    private async Task<List<Guid>> ClaimAsync(int free, CancellationToken ct)
    {
        if (free <= 0)
            return [];
        await using var db = await dbs.CreateDbContextAsync(ct);
        var queued = await db.Runs.Where(r => r.State == RunState.Queued).OrderBy(r => r.CreatedAt).Select(r => r.Id).Take(free).ToListAsync(ct);
        var claimed = new List<Guid>();
        foreach (var id in queued)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (await db.Runs.Where(r => r.Id == id && r.State == RunState.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RunState.Running).SetProperty(r => r.StartedAt, now).SetProperty(r => r.HeartbeatAt, now), ct) == 1)
                claimed.Add(id);
        }
        return claimed;
    }

    private async Task FailStaleAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = now.AddSeconds(-_options.StaleAfterSeconds);
        await using var db = await dbs.CreateDbContextAsync(ct);
        var ids = await db.Runs.Where(r => r.State == RunState.Running && r.HeartbeatAt < stale).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await db.StepRuns.Where(s => s.RunId == id && (s.State == StepState.Pending || s.State == StepState.Running))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Skipped), ct);
            await db.Runs.Where(r => r.Id == id && r.State == RunState.Running && r.HeartbeatAt < stale)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.State, RunState.Failed)
                    .SetProperty(r => r.Failure, "The server running this investigation stopped; run it again.")
                    .SetProperty(r => r.EndedAt, now), ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Looking for queued investigation runs failed; retrying on the next poll")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);
}
