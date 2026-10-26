using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>A valid plugin server file and the plugin that owns it (headers by name only).</summary>
internal sealed record PluginServerEntry(string Plugin, PluginServerDraft Server);
