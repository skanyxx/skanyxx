namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>
/// A header as the page shows it: where its value comes from, never the value. <see cref="Source"/> is
/// <see cref="HeaderSources.Value"/> (typed on the page, kept in <see cref="SecretName"/> by kagent) or
/// <see cref="HeaderSources.Secret"/> (an existing Secret).
/// </summary>
public sealed record HeaderView(string Name, string Source, string SecretName, string SecretKey);
