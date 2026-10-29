using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>The catalog of installation settings (D350): the only keys the Settings page and API accept.</summary>
public static class InstallationSettings
{
    public const string General = "General";
    public const string SignIn = "Sign-in";
    public const string Features = "Features";
    public const string Retention = "Retention";

    public const string InstallationName = "general.installationName";
    public const string DefaultTheme = "general.defaultTheme";
    public const string LandingPrefix = "general.landing.";
    public const string Entra = "signin.entra";
    public const string CloudTools = "features.cloudTools";
    public const string AuditDays = "retention.auditDays";

    /// <summary>The audit trail is kept at least this long whatever is saved (D376): the owner cannot erase their own recent actions.</summary>
    public const int MinAuditDays = 90;
    public const string ChatDays = "retention.chatDays";

    private const string Now = "At once, on every replica within 30 seconds.";

    /// <summary>The landing roles in the order a person with several roles is matched.</summary>
    public static readonly IReadOnlyList<string> LandingRoles =
        [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder, SkanyxxRoles.Employee];

    public static readonly IReadOnlyList<InstallationSetting> All =
    [
        new(InstallationName, General, "Installation name", "Shown under the logo and in the browser tab. Empty: \"Agent Workspace\".",
            SettingKind.Text, "", "Skanyxx:InstallationName", Now, MaxLength: 60),
        new(DefaultTheme, General, "Default theme", "What people get until they choose their own theme on their Account page.",
            SettingKind.Choice, "system", null, "At once, for everyone who has not chosen a theme.", PreferenceCatalog.Themes),
        Landing(SkanyxxRoles.Owner, "dashboard"),
        Landing(SkanyxxRoles.Supervisor, "dashboard"),
        Landing(SkanyxxRoles.Builder, "auto"),
        Landing(SkanyxxRoles.Employee, "auto"),
        new(Entra, SignIn, "Microsoft sign-in (Entra ID)",
            "Off: no Microsoft page, button, link or group re-check; managed accounts use their password. Configure it on the Microsoft sign-in page once on.",
            SettingKind.Bool, "false", "Identity:EntraAvailable", Now),
        new(CloudTools, Features, "Cloud tools", "A paid feature: it can be turned on only when this install's licence includes it.",
            SettingKind.Bool, "false", "Features:CloudTools", Now),
        new(AuditDays, Retention, "Audit history (days)", "Audit rows and finished invites older than this are deleted.",
            SettingKind.Number, "365", "Identity:AuditRetentionDays", "At the next retention run (within 6 hours).", Min: MinAuditDays, Max: 3650),
        new(ChatDays, Retention, "Chat history index (days)",
            "Chat sessions in the related-work index older than this are deleted. 0 keeps them. Transcripts are kagent's and follow its own TTL.",
            SettingKind.Number, "0", "ChatContext:SessionRetentionDays", "At the next chat purge (within 6 hours).", Min: 0, Max: 3650),
    ];

    public static InstallationSetting? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    public static string LandingKey(string role) => LandingPrefix + role;

    /// <summary>Why <paramref name="value"/> is not allowed for <paramref name="setting"/>; null when it is.</summary>
    public static string? Problem(InstallationSetting setting, string value) => setting.Kind switch
    {
        SettingKind.Bool when value is not ("true" or "false") => $"{setting.Label}: true or false.",
        SettingKind.Number when !int.TryParse(value, out var n) || n < setting.Min || n > setting.Max =>
            $"{setting.Label}: a whole number from {setting.Min} to {setting.Max}.",
        SettingKind.Choice when !setting.Choices!.Contains(value, StringComparer.Ordinal) =>
            $"{setting.Label}: must be one of {string.Join(", ", setting.Choices!)}.",
        SettingKind.Text when value.Length > setting.MaxLength || value.Any(char.IsControl) || value.Trim() != value =>
            $"{setting.Label}: at most {setting.MaxLength} characters, no control characters or surrounding spaces.",
        _ => null
    };

    /// <summary>The audit row's details: key and old/new values, or only that it changed for a secret.</summary>
    public static object AuditDetails(InstallationSetting setting, string? old, string @new) =>
        setting.Secret ? new { key = setting.Key, changed = true } : new { key = setting.Key, old, @new };

    private static InstallationSetting Landing(string role, string @default) =>
        new(LandingKey(role), General, $"Landing page: {role}",
            $"Where a {role} lands after sign-in unless they chose their own. A page their roles cannot open falls back to Chat.",
            SettingKind.Choice, @default, $"Skanyxx:Landing:{role}", "At their next visit to the home page.", PreferenceCatalog.Landings);
}
