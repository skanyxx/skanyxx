namespace Skanyxx.Core.Platform.Studio;

/// <param name="ModelConfigs">ModelConfigs the owner configured in kagent (names in the agents' namespace).</param>
/// <param name="McpServers">The allow-listed RemoteMCPServers besides memory, with the tools kagent discovered.</param>
/// <param name="Scopes">Memory scopes a grant may name: <c>company</c> and every team and department.</param>
public sealed record StudioFormOptions(
    IReadOnlyList<string> ModelConfigs, IReadOnlyList<McpToolChoice> McpServers, IReadOnlyList<string> Scopes)
{
    /// <summary>Servers that call a cloud provider's API: a paid feature (D364).</summary>
    public IReadOnlyList<string> PaidServers { get; init; } = [];

    /// <summary>Cloud tools are on (licence and the Settings switch, D370); when off, <see cref="PaidServers"/> are shown locked.</summary>
    public bool CloudToolsLicensed { get; init; }
}
