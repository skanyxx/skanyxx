using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.ChatContext.Data;

/// <summary>
/// D350: a minute after start and then every <see cref="Interval"/>, deletes indexed sessions not updated for
/// <see cref="ChatContextOptions.SessionRetentionDays"/> (0 keeps them). Read at each run, so the Settings page's
/// value applies without a restart. Plain DELETEs in batches of <see cref="BatchSize"/>: safe on every replica at once.
/// </summary>
internal sealed class SessionRetention(
    IServiceScopeFactory scopes, IOptionsMonitor<ChatContextOptions> options, TimeProvider time, ILogger<SessionRetention> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public const int BatchSize = 1000;

    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstRun, stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The chat session purge failed; the next run tries again");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>The number of sessions deleted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var days = options.CurrentValue.SessionRetentionDays;
        if (days == 0)
            return 0;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-days);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatContextDbContext>();
        var deleted = 0;
        int batch;
        do
        {
            // In batches, so turning retention on for a large index is many short deletes, not one long one.
            batch = await db.Sessions.Where(s => s.UpdatedAt < cutoff).OrderBy(s => s.Id).Take(BatchSize).ExecuteDeleteAsync(ct);
            deleted += batch;
        } while (batch == BatchSize);
        if (deleted > 0)
            logger.LogInformation("Deleted {Count} chat sessions not updated since {Cutoff} ({Days} days)", deleted, cutoff, days);
        return deleted;
    }
}
