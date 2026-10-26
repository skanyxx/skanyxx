namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>A header a plugin server is sent, as <c>plugin.yaml</c> records it: its name and the plugin's template, never a value.</summary>
internal sealed record PluginHeaderTemplate(string Server, string Name, string? Template);
