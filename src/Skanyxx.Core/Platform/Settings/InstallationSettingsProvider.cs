using Microsoft.Extensions.Configuration;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>
/// The saved installation settings as configuration (D350). Added last, so a saved value wins over appsettings and the
/// environment, and a key with nothing saved falls through to them. The identity module fills it after start and on
/// every save or poll; a change raises the configuration's reload token, so <c>IOptionsMonitor</c> readers see it.
/// </summary>
public sealed class InstallationSettingsProvider : ConfigurationProvider
{
    /// <summary>
    /// Replaces the saved values (setting key → value). Unknown keys are ignored. A number is held to its row's range
    /// (D387): a value saved before a floor existed (an audit history under 90 days) applies as the floor, not as saved.
    /// </summary>
    public void Apply(IReadOnlyDictionary<string, string> saved)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in saved)
            if (InstallationSettings.Find(key) is { ConfigKey: { } configKey } setting)
                data[configKey] = setting.Kind == SettingKind.Number && int.TryParse(value, out var n)
                    ? Math.Clamp(n, setting.Min, setting.Max).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : value;

        var current = Data;
        if (current.Count == data.Count && data.All(d => current.TryGetValue(d.Key, out var v) && v == d.Value))
            return;
        Data = data;
        OnReload();
    }
}
