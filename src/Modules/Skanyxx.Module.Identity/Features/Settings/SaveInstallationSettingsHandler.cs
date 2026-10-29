using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;
using Skanyxx.Core.Platform.Settings;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Features.Preferences;

namespace Skanyxx.Module.Identity.Features.Settings;

/// <summary>
/// Writes the values that differ from the effective ones, one audit row each (key, old, new; a secret only that it
/// changed), in one transaction; then this replica applies them at once (D350). The default theme is the
/// organization's preferences row, so the Account page's "organization default" and this field are one value.
/// </summary>
internal sealed class SaveInstallationSettingsHandler(
    AccountsDbContext db, IConfiguration configuration, IdentityAudit audit, InstallationSettingsRefresher refresher,
    ClientAddress client, TimeProvider time, ILogger<SaveInstallationSettingsHandler> logger)
    : IRequestHandler<SaveInstallationSettingsCommand, Outcome<IReadOnlyList<string>>>
{
    public async Task<Outcome<IReadOnlyList<string>>> Handle(SaveInstallationSettingsCommand command, CancellationToken ct)
    {
        if (command.Values.GetValueOrDefault(InstallationSettings.CloudTools) == "true" && !CloudToolsFeature.IsLicensed(configuration))
            return Outcome<IReadOnlyList<string>>.Conflict([], "Cloud tools is a paid feature that this install's licence does not include.", "unlicensed");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await EffectiveSettings.ReadAsync(db, configuration, ct);
        var changed = new List<string>();
        foreach (var (key, value) in command.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            var setting = InstallationSettings.Find(key)!;
            var old = current[key];
            if (!setting.Secret && old == value)
                continue;
            if (key == InstallationSettings.DefaultTheme)
                await SaveThemeAsync(command.ActorId, value, ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO identity_installation_settings ("Key", "Value", "UpdatedBy", "UpdatedUtc")
                    VALUES ({key}, {value}, {command.ActorId}, {time.GetUtcNow()})
                    ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value", "UpdatedBy" = EXCLUDED."UpdatedBy", "UpdatedUtc" = EXCLUDED."UpdatedUtc"
                    """, ct);
            await audit.WriteAsync(AuditActions.SettingChanged, command.ActorId, key, InstallationSettings.AuditDetails(setting, old, value), ct);
            changed.Add(key);
        }
        await transaction.CommitAsync(ct);

        if (changed.Count > 0)
        {
            logger.LogWarning("Installation settings {Keys} changed by {ActorUserId} from {RemoteIp}", changed, command.ActorId, client.Current);
            await refresher.ReloadAsync(ct);
        }
        return Outcome<IReadOnlyList<string>>.Ok(changed);
    }

    private async Task SaveThemeAsync(string actorId, string theme, CancellationToken ct)
    {
        var json = await db.OrganizationPreferences.AsNoTracking().Select(p => p.Json).FirstOrDefaultAsync(ct);
        var set = PreferenceJson.Read(json) with { Theme = theme };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_organization_preferences ("Id", "Json", "UpdatedBy", "UpdatedUtc")
            VALUES ({OrganizationPreference.SingletonId}, CAST({PreferenceJson.Write(set)} AS jsonb), {actorId}, {time.GetUtcNow()})
            ON CONFLICT ("Id") DO UPDATE SET "Json" = EXCLUDED."Json", "UpdatedBy" = EXCLUDED."UpdatedBy", "UpdatedUtc" = EXCLUDED."UpdatedUtc"
            """, ct);
    }
}
