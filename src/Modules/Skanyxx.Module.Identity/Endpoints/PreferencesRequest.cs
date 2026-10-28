using System.Text.Json.Serialization;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>
/// A <see cref="PreferenceSet"/> as sent. An unknown key is refused (400), not ignored, so a typo or a smuggled field
/// (<c>userId</c>, <c>css</c>) never looks like it was saved.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PreferencesRequest
{
    public string? Theme { get; set; }
    public string? Accent { get; set; }
    public string? Density { get; set; }
    public string? FontSize { get; set; }
    public string? Sidebar { get; set; }
    public List<string>? PinnedNav { get; set; }
    public List<string>? NavOrder { get; set; }
    public string? Landing { get; set; }
    public string? ChatDetails { get; set; }
    public int? TablePageSize { get; set; }
    public string? Motion { get; set; }

    public PreferenceSet ToSet() =>
        new(Theme, Accent, Density, FontSize, Sidebar, PinnedNav, NavOrder, Landing, ChatDetails, TablePageSize, Motion);
}
