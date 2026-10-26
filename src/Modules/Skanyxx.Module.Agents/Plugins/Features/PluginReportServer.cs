namespace Skanyxx.Module.Agents.Plugins.Features;

internal sealed record PluginReportServer(string Name, string Description, string Url, string Protocol, IReadOnlyList<PluginReportHeader> Headers);
