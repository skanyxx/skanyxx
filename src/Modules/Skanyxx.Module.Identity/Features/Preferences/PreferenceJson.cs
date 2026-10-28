using System.Text.Json;
using System.Text.Json.Serialization;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Features.Preferences;

/// <summary>
/// How a <see cref="PreferenceSet"/> is stored. Reading re-checks it against <see cref="PreferenceCatalog"/>: a row
/// written by an older version with a value since retired falls back to inheriting instead of reaching the page.
/// </summary>
internal static class PreferenceJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Write(PreferenceSet set) => JsonSerializer.Serialize(set, Options);

    public static PreferenceSet Read(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return PreferenceSet.Empty;
        try
        {
            var set = JsonSerializer.Deserialize<PreferenceSet>(json, Options) ?? PreferenceSet.Empty;
            return PreferenceCatalog.Problems(set).Count == 0 ? set : PreferenceSet.Empty;
        }
        catch (JsonException)
        {
            return PreferenceSet.Empty;
        }
    }
}
