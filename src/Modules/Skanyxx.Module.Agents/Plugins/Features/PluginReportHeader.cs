namespace Skanyxx.Module.Agents.Plugins.Features;

/// <param name="Template">What the owner is to enter (<c>Bearer ${GITHUB_TOKEN}</c>); null when the plugin carries the value.</param>
/// <param name="Value"><see cref="PluginReport.ValueIncluded"/> when the plugin carries a value (stored as a secret, never shown); else null.</param>
internal sealed record PluginReportHeader(string Name, string? Template, string? Value);
