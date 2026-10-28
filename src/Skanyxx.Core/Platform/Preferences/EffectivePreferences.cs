namespace Skanyxx.Core.Platform.Preferences;

/// <summary>What the layout applies: every field set, resolved from the person's choices over the organization's defaults.</summary>
public sealed record EffectivePreferences(
    string Theme,
    string Accent,
    string Density,
    string FontSize,
    string Sidebar,
    IReadOnlyList<string> PinnedNav,
    IReadOnlyList<string> NavOrder,
    string Landing,
    string ChatDetails,
    int TablePageSize,
    string Motion)
{
    public static readonly EffectivePreferences Defaults = new(
        Theme: "system", Accent: "auto", Density: "comfortable", FontSize: "m", Sidebar: "expanded",
        PinnedNav: [], NavOrder: [], Landing: "auto", ChatDetails: "shown", TablePageSize: 25, Motion: "system");

    public static EffectivePreferences Resolve(PreferenceSet? organization, PreferenceSet? person)
    {
        var org = organization ?? PreferenceSet.Empty;
        var own = person ?? PreferenceSet.Empty;
        return new(
            own.Theme ?? org.Theme ?? Defaults.Theme,
            own.Accent ?? org.Accent ?? Defaults.Accent,
            own.Density ?? org.Density ?? Defaults.Density,
            own.FontSize ?? org.FontSize ?? Defaults.FontSize,
            own.Sidebar ?? org.Sidebar ?? Defaults.Sidebar,
            own.PinnedNav ?? org.PinnedNav ?? Defaults.PinnedNav,
            own.NavOrder ?? org.NavOrder ?? Defaults.NavOrder,
            own.Landing ?? org.Landing ?? Defaults.Landing,
            own.ChatDetails ?? org.ChatDetails ?? Defaults.ChatDetails,
            own.TablePageSize ?? org.TablePageSize ?? Defaults.TablePageSize,
            own.Motion ?? org.Motion ?? Defaults.Motion);
    }
}
