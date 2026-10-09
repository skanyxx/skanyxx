using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Audit;

/// <summary>
/// D155: shortly after start and then every <see cref="Interval"/>, deletes audit rows older than
/// <see cref="IdentityModuleOptions.AuditRetentionDays"/>, invites that stopped being pending (accepted, revoked or
/// expired) before that, and password-reset links a day after they expired. Plain DELETEs in batches of
/// <see cref="BatchSize"/>, safe on every replica at once.
/// What it removed is itself an audit row, so the trail shows its own gaps.
/// </summary>
internal sealed class IdentityRetention(
    IServiceScopeFactory scopes, IOptions<IdentityModuleOptions> options, TimeProvider time, ILogger<IdentityRetention> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

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
                logger.LogWarning(ex, "The identity retention purge failed; the next run tries again");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Rows per DELETE (D169): the first run after an upgrade, or after lowering the retention, is many small statements, not one huge one.</summary>
    internal int BatchSize { get; set; } = 5000;

    internal async Task<(int Audit, int Invites, int Resets)> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = time.GetUtcNow();
        var cutoff = now.AddDays(-options.Value.AuditRetentionDays);
        var dayAgo = now.AddDays(-1);

        var audit = await InBatchesAsync(() => db.Audit.Where(a => a.AtUtc < cutoff).OrderBy(a => a.Id).Take(BatchSize).ExecuteDeleteAsync(ct));
        // Pending invites expire in the future, so the cutoff (in the past) never reaches one.
        var invites = await InBatchesAsync(() => db.Invites
            .Where(i => (i.AcceptedUtc ?? i.RevokedUtc ?? i.ExpiresUtc) < cutoff).OrderBy(i => i.Id).Take(BatchSize).ExecuteDeleteAsync(ct));
        var resets = await InBatchesAsync(() => db.PasswordResets.Where(r => r.ExpiresUtc < dayAgo).OrderBy(r => r.Id).Take(BatchSize).ExecuteDeleteAsync(ct));
        // After the batches, not with them: each batch commits on its own, so a purge cut short still leaves its row next time.
        if (audit + invites > 0)
            await scope.ServiceProvider.GetRequiredService<IdentityAudit>().WriteAsync(AuditActions.RetentionPurged, null, null,
                new { olderThan = cutoff, auditRows = audit, invites, retentionDays = options.Value.AuditRetentionDays }, ct);

        if (audit + invites + resets > 0)
            logger.LogInformation("Identity retention removed {Audit} audit rows and {Invites} invites older than {Cutoff}, and {Resets} expired reset links",
                audit, invites, cutoff, resets);
        return (audit, invites, resets);
    }

    private async Task<int> InBatchesAsync(Func<Task<int>> batch)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await batch();
            total += deleted;
        }
        while (deleted == BatchSize);
        return total;
    }
}
