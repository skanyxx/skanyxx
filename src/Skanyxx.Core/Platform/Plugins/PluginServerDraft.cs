namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// A remote MCP server from a plugin, as a kagent RemoteMCPServer. <see cref="Protocol"/> is <c>SSE</c> or
/// <c>STREAMABLE_HTTP</c>. Every header is sent from a Secret, never written to git.
/// </summary>
public sealed record PluginServerDraft(
    string Name,
    string Description,
    string Protocol,
    string Url,
    IReadOnlyList<PluginHeader> Headers);
