namespace Skanyxx.Core.Platform.Studio;

/// <param name="ModelConfigs">ModelConfigs the owner configured in kagent (names in the agents' namespace).</param>
/// <param name="McpServers">The allow-listed RemoteMCPServers besides memory, with the tools kagent discovered.</param>
/// <param name="Scopes">Memory scopes a grant may name: <c>company</c> and every team and department.</param>
public sealed record StudioFormOptions(
    IReadOnlyList<string> ModelConfigs, IReadOnlyList<McpToolChoice> McpServers, IReadOnlyList<string> Scopes);
