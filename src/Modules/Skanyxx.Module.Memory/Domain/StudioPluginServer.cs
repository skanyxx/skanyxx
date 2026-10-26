namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// A plugin's remote MCP server as the studio knows it: the header values it is sent (protected with Data Protection,
/// never in git), and what kagent was last given, because kagent offers no read of a server's spec or Secret.
/// </summary>
public sealed class StudioPluginServer
{
    public required string Server { get; set; }
    public required string Plugin { get; set; }

    /// <summary>The header values as protected JSON (name → value); null when none are stored.</summary>
    public string? ProtectedValues { get; set; }

    /// <summary>The header names a value is stored for (names are not secret; the overview shows them without decrypting).</summary>
    public string[] ValueNames { get; set; } = [];

    /// <summary>Scheme, host, port and path the values were entered for; they are never sent anywhere else (D220).</summary>
    public string? ValueAddress { get; set; }

    /// <summary>Names the stored set (random, new when it changes; D220).</summary>
    public string? ValueFingerprint { get; set; }
    public string? DeployedFingerprint { get; set; }
    public string? DeployedSpecHash { get; set; }

    /// <summary>The studio created it in kagent (and so may replace or delete it).</summary>
    public bool Claimed { get; set; }

    public DateTime UpdatedAt { get; set; }
}
