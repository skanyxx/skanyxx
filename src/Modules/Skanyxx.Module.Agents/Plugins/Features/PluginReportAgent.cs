using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <param name="Tools">Per plugin server, the tools it may call; none listed = every tool of that server.</param>
internal sealed record PluginReportAgent(string Name, string Description, string ModelConfig, IReadOnlyList<McpToolChoice> Tools);
