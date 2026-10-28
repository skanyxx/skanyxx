using Skanyxx.Module.ToolServers.Data;

namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>
/// What was detected, by name only: addresses without paths, Secret names and keys, never a value.
/// </summary>
public sealed record FixFacts(string KAgentAddress)
{
    public string? Namespace { get; init; }

    public string? Name { get; init; }

    public string? Kind { get; init; }

    /// <summary>Scheme, host and port of the server URL; the path is left out (it may carry a token).</summary>
    public string? UrlOrigin { get; init; }

    public bool LoopbackUrl { get; init; }

    public string? Protocol { get; init; }

    /// <summary>Headers read from a Secret someone else keeps.</summary>
    public IReadOnlyList<ToolServerHeader> SecretHeaders { get; init; } = [];

    public int? ToolCount { get; init; }

    public IReadOnlyList<string>? UsedBy { get; init; }

    public int? KAgentStatus { get; init; }

    public string? Ref => Name is null ? null : $"{Namespace}/{Name}";
}
