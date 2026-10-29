namespace Skanyxx.Core.Platform.Settings;

/// <summary>
/// One installation-wide setting on the owner's Settings page (D350). Saved values live in the identity database and
/// reach the rest of the app as the configuration key <paramref name="ConfigKey"/>, so code reads them like any other
/// configuration and a deploy-time value applies until the owner saves one. A null <paramref name="ConfigKey"/> is a
/// setting stored elsewhere (the default theme is the organization's preferences).
/// </summary>
/// <param name="Secret">Never shown and never written to the audit (only that it changed).</param>
/// <param name="TakesEffect">When a save is felt, in plain words; the page says it next to the field.</param>
public sealed record InstallationSetting(
    string Key,
    string Section,
    string Label,
    string Hint,
    SettingKind Kind,
    string Default,
    string? ConfigKey,
    string TakesEffect,
    IReadOnlyList<string>? Choices = null,
    int Min = 0,
    int Max = 0,
    int MaxLength = 0,
    bool Secret = false);
