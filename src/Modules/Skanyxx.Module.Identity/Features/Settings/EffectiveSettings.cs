using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform.Settings;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Features.Preferences;

namespace Skanyxx.Module.Identity.Features.Settings;

/// <summary>
/// What each setting is now: the saved row (read from the database, never this replica's possibly older copy), else
/// deployment configuration, else the default. Rows are never deleted, so a key with no row has no saved value anywhere.
/// </summary>
internal static class EffectiveSettings
{
    public static async Task<Dictionary<string, string>> ReadAsync(AccountsDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var orgJson = await db.OrganizationPreferences.AsNoTracking().Select(p => p.Json).FirstOrDefaultAsync(ct);
        var theme = PreferenceJson.Read(orgJson).Theme;
        var saved = await db.InstallationSettings.AsNoTracking().ToDictionaryAsync(r => r.Key, r => r.Value, ct);
        return InstallationSettings.All.ToDictionary(s => s.Key, s => s.Secret ? "" : s.Key == InstallationSettings.DefaultTheme
            ? theme ?? s.Default
            : Normalize(s, saved.GetValueOrDefault(s.Key) ?? (s.ConfigKey is { } key ? configuration[key] : null)));
    }

    private static string Normalize(InstallationSetting setting, string? value) => setting.Kind switch
    {
        _ when value is null => setting.Default,
        SettingKind.Bool => bool.TryParse(value, out var on) ? (on ? "true" : "false") : setting.Default,
        _ => value
    };
}
