using Microsoft.Extensions.Configuration;

namespace Skanyxx.Core.Platform.Settings;

/// <summary>
/// The saved installation settings as configuration (D350). Added last, so a saved value wins over appsettings and the
/// environment, and a key with nothing saved falls through to them. The identity module fills it after start and on
/// every save or poll; a change raises the configuration's reload token, so <c>IOptionsMonitor</c> readers see it.
/// </summary>
public sealed class InstallationSettingsProvider : ConfigurationProvider
{
    /// <summary>Replaces the saved values (setting key → value). Unknown keys are ignored.</summary>
    public void Apply(IReadOnlyDictionary<string, string> saved)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in saved)
            if (InstallationSettings.Find(key)?.ConfigKey is { } configKey)
                data[configKey] = value;

        var current = Data;
        if (current.Count == data.Count && data.All(d => current.TryGetValue(d.Key, out var v) && v == d.Value))
            return;
        Data = data;
        OnReload();
    }
}
