namespace Skanyxx.Core.Platform.Plugins;

/// <param name="Template">What the plugin says to put there (<c>Bearer ${GITHUB_TOKEN}</c>); null for a value it carried itself.</param>
/// <param name="HasValue">Skanyxx holds a value for it (the value itself is never shown).</param>
public sealed record PluginHeaderSummary(string Name, string? Template, bool HasValue);
