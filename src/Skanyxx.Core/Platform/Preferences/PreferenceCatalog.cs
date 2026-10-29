namespace Skanyxx.Core.Platform.Preferences;

/// <summary>
/// The allow-list for <see cref="PreferenceSet"/>. Values become data attributes on the page's root element, so only
/// these fixed words (and page sizes) are ever accepted or rendered. The nav keys name the Host's panels; which of them
/// a person may see is decided by their roles when the nav is drawn, never by what is stored here.
/// </summary>
public static class PreferenceCatalog
{
    public static readonly IReadOnlyList<string> Themes = ["system", "light", "dark", "blue", "green", "purple", "orange", "teal", "rose"];
    public static readonly IReadOnlyList<string> Accents = ["auto", "indigo", "blue", "teal", "green", "orange", "rose", "purple"];
    public static readonly IReadOnlyList<string> Densities = ["comfortable", "compact"];
    public static readonly IReadOnlyList<string> FontSizes = ["s", "m", "l"];
    public static readonly IReadOnlyList<string> Sidebars = ["expanded", "collapsed"];
    public static readonly IReadOnlyList<string> ChatDetailModes = ["shown", "hidden"];
    public static readonly IReadOnlyList<string> MotionModes = ["system", "reduce"];
    public static readonly IReadOnlyList<int> TablePageSizes = [10, 25, 50, 100];

    public static readonly IReadOnlyList<string> NavKeys =
    [
        "chat", "library", "agents", "studio", "plugins", "sandboxes", "investigate",
        "dashboard", "models", "toolservers", "people", "org", "entra", "audit", "analytics", "setupguides", "settings", "debug", "quotas"
    ];

    /// <summary>"auto" is the built-in landing (Chat; the owner's model step until a model is set).</summary>
    public static readonly IReadOnlyList<string> Landings = ["auto", .. NavKeys];

    public const int MaxPinned = 6;

    /// <summary>Each problem as "field: why", empty when every set field is allowed.</summary>
    public static IReadOnlyList<string> Problems(PreferenceSet set)
    {
        var problems = new List<string>();
        void OneOf<T>(string field, T? value, IReadOnlyList<T> allowed) where T : struct
        {
            if (value is { } v && !allowed.Contains(v))
                problems.Add($"{field}: must be one of {string.Join(", ", allowed)}.");
        }
        void Word(string field, string? value, IReadOnlyList<string> allowed)
        {
            if (value is not null && !allowed.Contains(value, StringComparer.Ordinal))
                problems.Add($"{field}: must be one of {string.Join(", ", allowed)}.");
        }
        void Keys(string field, IReadOnlyList<string>? keys, int max)
        {
            if (keys is null) return;
            if (keys.Count > max)
                problems.Add($"{field}: at most {max} items.");
            if (keys.Any(k => k is null || !NavKeys.Contains(k, StringComparer.Ordinal)))
                problems.Add($"{field}: every item must be one of {string.Join(", ", NavKeys)}.");
            else if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
                problems.Add($"{field}: an item appears twice.");
        }

        Word("theme", set.Theme, Themes);
        Word("accent", set.Accent, Accents);
        Word("density", set.Density, Densities);
        Word("fontSize", set.FontSize, FontSizes);
        Word("sidebar", set.Sidebar, Sidebars);
        Keys("pinnedNav", set.PinnedNav, MaxPinned);
        Keys("navOrder", set.NavOrder, NavKeys.Count);
        Word("landing", set.Landing, Landings);
        Word("chatDetails", set.ChatDetails, ChatDetailModes);
        OneOf("tablePageSize", set.TablePageSize, TablePageSizes);
        Word("motion", set.Motion, MotionModes);
        return problems;
    }
}
