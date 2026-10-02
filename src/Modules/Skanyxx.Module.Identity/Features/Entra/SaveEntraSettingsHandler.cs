using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.Entra;

/// <summary>
/// Replaces the settings row and the whole group map in one transaction, one save at a time across replicas (an
/// advisory lock, so a first save racing another cannot collide on the key), bumps the version in SQL, and reloads this
/// instance's copy at once (other replicas within <see cref="EntraSettingsRefresher.Interval"/>): the next Microsoft
/// sign-in uses the new values, no restart. Every save gets its own version, so no replica can cache one save's content
/// under another's version (CR M1). Ids are stored as lowercase GUIDs. The secret is kept Data-Protection-protected
/// and never logged; an empty one keeps the stored secret. Logged at Warning with the actor and client address.
/// Turning Microsoft sign-in on (off → on) ends every session of every Entra-managed account but the owner's, in the
/// same transaction (D15): a password session opened while it was off must not outlive D8.
/// </summary>
internal sealed class SaveEntraSettingsHandler(
    AccountsDbContext db, EntraSettingsCache cache, EntraRedirectUri redirect, EntraKeyRing keyRing, ClientAddress client, TimeProvider time,
    ILogger<SaveEntraSettingsHandler> logger)
    : IRequestHandler<SaveEntraSettingsCommand, Outcome<EntraSettingsDto>>
{
    public const string NotReloaded = "Saved, but this server could not reload the settings yet: they apply within a minute.";

    // Listed with every other advisory-lock key in Skanyxx.Module.Memory's MemoryMigrator.
    private const long SaveLockKey = 0x49444E04;

    public async Task<Outcome<EntraSettingsDto>> Handle(SaveEntraSettingsCommand command, CancellationToken ct)
    {
        if (command.Enabled && !redirect.CanBuild)
            return Outcome<EntraSettingsDto>.Conflict(null, EntraRedirectUri.NotConfigured);

        var groups = command.Groups.Select(g => new EntraGroupMap
        {
            GroupId = EntraClaims.NormalizeId(g.GroupId)!,
            Label = string.IsNullOrWhiteSpace(g.Label) ? null : g.Label.Trim(),
            Roles = [.. g.Roles.Distinct().Order()],
            Teams = [.. g.Teams.Distinct().Order()]
        }).ToList();
        var newSecret = !string.IsNullOrEmpty(command.ClientSecret);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SaveLockKey})", ct);
        var teams = groups.SelectMany(g => g.Teams).Distinct().ToList();
        var known = await db.Teams.Where(t => teams.Contains(t.Slug)).Select(t => t.Slug).ToListAsync(ct);
        if (teams.Except(known).FirstOrDefault() is { } unknown)
            return Outcome<EntraSettingsDto>.NotFound($"No team '{unknown}'.");

        var row = await db.EntraSettings.SingleOrDefaultAsync(ct);
        var turnedOn = command.Enabled && row?.Enabled != true;
        if (row is null)
            db.EntraSettings.Add(row = new EntraSettings());
        if (newSecret)
            row.ProtectedClientSecret = cache.Protect(command.ClientSecret!);
        else if (command.Enabled && row.ProtectedClientSecret is null)
            throw new ValidationException([new ValidationFailure(nameof(command.ClientSecret), "Enter the client secret to turn Microsoft sign-in on.")]);

        row.Enabled = command.Enabled;
        row.TenantId = EntraClaims.NormalizeId(command.TenantId) ?? "";
        row.ClientId = EntraClaims.NormalizeId(command.ClientId) ?? "";
        row.UpdatedBy = command.ActorId;
        row.UpdatedUtc = time.GetUtcNow();
        await db.EntraGroups.ExecuteDeleteAsync(ct);
        db.EntraGroups.AddRange(groups);
        await db.SaveChangesAsync(ct);
        await db.EntraSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1), ct);
        var ended = turnedOn ? await EndManagedSessionsAsync(ct) : 0;
        await transaction.CommitAsync(ct);

        logger.LogWarning("Microsoft sign-in settings saved by {ActorUserId} from {RemoteIp}: {State}, tenant {TenantId}, client {ClientId}, " +
            "secret {Secret}, {GroupCount} mapped groups {Groups}",
            command.ActorId, client.Current, row.Enabled ? "on" : "off", row.TenantId, row.ClientId, newSecret ? "replaced" : "kept",
            groups.Count, groups.Select(g => $"{g.GroupId}=>[{string.Join(",", g.Roles.Concat(g.Teams.Select(t => "team:" + t)))}]"));
        if (turnedOn)
            logger.LogWarning("Microsoft sign-in turned on by {ActorUserId} from {RemoteIp}: the sessions of {Count} Entra-managed accounts ended",
                command.ActorId, client.Current, ended);
        if (newSecret && keyRing.Unencrypted)
            logger.LogWarning("Microsoft sign-in client secret stored by {ActorUserId}: {Warning}", command.ActorId, EntraSettingsDto.SecretKeysWarning);

        var reloaded = await ReloadAsync();
        return new Outcome<EntraSettingsDto>(OutcomeStatus.Ok, await EntraSettingsRows.ReadAsync(db, cache, redirect, keyRing, ct), reloaded ? null : NotReloaded);
    }

    /// <summary>
    /// D15: rotates the security stamp (and the concurrency stamp, so a request holding an older copy of the row fails
    /// instead of writing the old stamp back) and drops the refresh chains of every non-owner account with a Microsoft
    /// login. One statement each, under the save lock; the number of accounts is returned for the audit line.
    /// </summary>
    private async Task<int> EndManagedSessionsAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            DELETE FROM identity_refresh_sessions s
            WHERE EXISTS (SELECT 1 FROM identity_user_logins l WHERE l."UserId" = s."UserId" AND l."LoginProvider" = {EntraScheme.Name})
              AND NOT EXISTS (SELECT 1 FROM identity_user_roles r WHERE r."UserId" = s."UserId" AND r."RoleId" = {SkanyxxRoles.Owner})
            """, ct);
        return await db.Database.ExecuteSqlAsync($"""
            UPDATE identity_users u SET "SecurityStamp" = upper(replace(gen_random_uuid()::text, '-', '')), "ConcurrencyStamp" = gen_random_uuid()::text
            WHERE EXISTS (SELECT 1 FROM identity_user_logins l WHERE l."UserId" = u."Id" AND l."LoginProvider" = {EntraScheme.Name})
              AND NOT EXISTS (SELECT 1 FROM identity_user_roles r WHERE r."UserId" = u."Id" AND r."RoleId" = {SkanyxxRoles.Owner})
            """, ct);
    }

    /// <summary>
    /// Saved: this instance must pick it up even if the owner's browser has gone. A failure here does not undo the save;
    /// the refresher retries on its next tick, so the owner is told that rather than shown an error.
    /// </summary>
    private async Task<bool> ReloadAsync()
    {
        try
        {
            await cache.RefreshAsync(db, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Microsoft sign-in settings were saved, but reloading them on this server failed; the refresher retries");
            return false;
        }
    }
}
