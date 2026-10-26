namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>
/// How a plugin is translated: the ModelConfig every agent runs on (a plugin's own model choice is ignored, P5), whether
/// <c>http://</c> MCP server URLs are accepted, and whether they may name an internal address.
/// </summary>
internal sealed record PluginReaderOptions(string ModelConfig, bool AllowHttp, bool AllowPrivateServers = false);
