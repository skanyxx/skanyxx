using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// D158: every <see cref="IdentityModuleOptions.EntraRecheckMinutes"/> while Microsoft sign-in is on, asks Graph
/// (<c>checkMemberGroups</c>, app-only, the mapped ids only) which mapped groups each Entra-managed account is in now, and
/// applies the answer through <see cref="EntraAccounts.RecheckAsync"/>: re-mapped, or — no mapped group, or the user gone
/// from the tenant — refused like a sign-in (D2, D161). So a removal in Entra reaches a signed-in person within the
/// interval instead of at their next Microsoft sign-in. Skipped: the owner, disabled accounts, and keys of another tenant
/// (the configured tenant cannot answer for them). Graph unreachable: nothing changes, one Warning (an Error naming the
/// cause for a 403 — consent missing — or a 400 — a stale group id), the sweep stops until the next one.
/// D164: one sweep per interval across every replica. Each replica looks every few minutes (the first time a minute after
/// start); under the advisory lock it reads when the last sweep began (<c>identity_job_runs</c>), and only when that is
/// an interval ago does it record "now" (the database's clock, so replica clock skew does not matter) and commit — the lock is held for that claim only, not for the sweep, so no
/// transaction idles through Graph calls. A restart therefore never postpones an overdue sweep by a whole interval.
/// </summary>
internal sealed class EntraRecheck(
    IServiceScopeFactory scopes, EntraSettingsCache cache, IOptions<IdentityModuleOptions> options, TimeProvider time, ILogger<EntraRecheck> logger)
    : BackgroundService
{
    // Listed with every other advisory-lock key in Skanyxx.Module.Memory's MemoryMigrator.
    internal const long SweepLockKey = 0x49444E06;

    internal const string JobName = "entra-recheck";

    private static readonly TimeSpan FirstLook = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan LookEvery = TimeSpan.FromMinutes(5);

    private TimeSpan Interval => TimeSpan.FromMinutes(options.Value.EntraRecheckMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstLook, time, stoppingToken);
        using var timer = new PeriodicTimer(Interval < LookEvery ? Interval : LookEvery, time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The Entra group re-check failed; the next one tries again");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One sweep when one is due; the number of accounts that changed, or null when it did not run.</summary>
    internal async Task<int?> RunOnceAsync(CancellationToken ct)
    {
        var config = cache.Current;
        if (!config.CanSignIn || !await ClaimAsync(ct))
            return null;
        return await SweepAsync(config, ct);
    }

    /// <summary>Under the sweep lock, briefly: true (and the sweep recorded as begun now) when the last one began an interval ago.</summary>
    private async Task<bool> ClaimAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({SweepLockKey}) AS \"Value\"").SingleAsync(ct))
            return false;

        // The database's clock, not this replica's: a replica running ahead must not postpone the others' sweeps.
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_job_runs ("Name", "LastRunUtc") VALUES ({JobName}, now())
            ON CONFLICT ("Name") DO UPDATE SET "LastRunUtc" = EXCLUDED."LastRunUtc"
            WHERE identity_job_runs."LastRunUtc" <= now() - {Interval}
            """, ct) == 1;
        await transaction.CommitAsync(ct);
        return claimed;
    }

    private async Task<int> SweepAsync(EntraConfig config, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var accounts = await db.UserLogins.AsNoTracking()
            .Where(l => l.LoginProvider == EntraScheme.Name
                && !db.UserRoles.Any(r => r.UserId == l.UserId && r.RoleId == SkanyxxRoles.Owner)
                && db.Users.Any(u => u.Id == l.UserId && (u.LockoutEnd == null || u.LockoutEnd < AccountStatus.DisabledUntil)))
            .OrderBy(l => l.UserId)
            .Select(l => new { l.UserId, l.ProviderKey })
            .ToListAsync(ct);

        var mapper = scope.ServiceProvider.GetRequiredService<EntraMapper>();
        var entra = scope.ServiceProvider.GetRequiredService<EntraAccounts>();
        var (changed, foreign) = (0, 0);
        foreach (var account in accounts)
        {
            if (!TryParseKey(account.ProviderKey, out var tenant, out var objectId) || tenant != config.TenantId)
            {
                foreign++;
                continue;
            }

            EntraMapping mapping;
            try
            {
                mapping = await mapper.RecheckAsync(objectId, config, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                logger.LogError(ex, "The Entra group re-check stopped: Graph answered {Status} ({Cause}); nothing was changed, and every sweep fails until it is fixed",
                    (int)ex.StatusCode!.Value, ex.StatusCode == HttpStatusCode.Forbidden
                        ? "the app registration lacks the application permission GroupMember.Read.All with admin consent"
                        : "a mapped group id is probably stale or malformed: check the group map on the Entra page");
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "The Entra group re-check could not ask Graph ({Account}); nothing was changed, the next sweep tries again", account.UserId);
                break;
            }

            try
            {
                if (await entra.RecheckAsync(account.UserId, account.ProviderKey, mapping, ct))
                    changed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The change itself is saved (PrivilegeRevocation logged the failed publish at Error); the next account goes on.
                logger.LogWarning(ex, "The Entra group re-check of {UserId} did not finish", account.UserId);
            }
        }

        if (foreign > 0)
            logger.LogWarning("The Entra group re-check skipped {Count} accounts whose Microsoft login belongs to another tenant than {TenantId}",
                foreign, config.TenantId);
        logger.LogInformation("Entra group re-check: {Checked} accounts, {Changed} changed", accounts.Count - foreign, changed);
        return changed;
    }

    private static bool TryParseKey(string key, out string tenant, out Guid objectId)
    {
        var parts = key.Split('|');
        tenant = parts.Length == 2 && EntraClaims.NormalizeId(parts[0]) is { } t ? t : "";
        objectId = Guid.Empty;
        return tenant.Length > 0 && Guid.TryParse(parts[1], out objectId);
    }
}
