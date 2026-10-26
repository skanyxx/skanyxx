namespace Skanyxx.Core.Platform.Plugins;

/// <param name="State">One of <see cref="PluginServerStates"/>.</param>
public sealed record PluginServerSummary(string Name, string Url, string Protocol, string State, IReadOnlyList<PluginHeaderSummary> Headers);
