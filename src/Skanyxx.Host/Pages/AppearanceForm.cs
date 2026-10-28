using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Host.Pages;

/// <summary>The Account page's appearance form as posted. The values are checked against <see cref="PreferenceCatalog"/> by the save.</summary>
public sealed class AppearanceForm
{
    public string? Theme { get; set; }
    public string? Accent { get; set; }
    public string? Density { get; set; }
    public string? FontSize { get; set; }
    public string? Sidebar { get; set; }
    public List<string> PinnedNav { get; set; } = [];

    /// <summary>Comma-separated nav keys, written by the page's move buttons.</summary>
    public string? NavOrder { get; set; }

    public string? Landing { get; set; }
    public string? ChatDetails { get; set; }
    public int? TablePageSize { get; set; }
    public string? Motion { get; set; }

    public PreferenceSet ToSet() => new(
        Blank(Theme), Blank(Accent), Blank(Density), Blank(FontSize), Blank(Sidebar),
        PinnedNav,
        NavOrder?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
        Blank(Landing), Blank(ChatDetails), TablePageSize, Blank(Motion));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
