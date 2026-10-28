namespace Skanyxx.Core.Platform.Preferences;

/// <summary>
/// Appearance and layout choices as stored: a person's own, or the organization's defaults. A null field is not set
/// and inherits (person ← organization ← <see cref="EffectivePreferences.Defaults"/>). Every value is one of
/// <see cref="PreferenceCatalog"/>'s: nothing here is free-form, so nothing stored can carry CSS, a URL or script.
/// </summary>
public sealed record PreferenceSet(
    string? Theme = null,
    string? Accent = null,
    string? Density = null,
    string? FontSize = null,
    string? Sidebar = null,
    IReadOnlyList<string>? PinnedNav = null,
    IReadOnlyList<string>? NavOrder = null,
    string? Landing = null,
    string? ChatDetails = null,
    int? TablePageSize = null,
    string? Motion = null)
{
    public static readonly PreferenceSet Empty = new();
}
