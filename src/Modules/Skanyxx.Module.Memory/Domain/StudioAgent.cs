namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// A memory principal the studio claimed (D117): only these are the studio's to grant, issue for and remove. Claimed on
/// the studio's first write for a name memory knew nothing about.
/// </summary>
public sealed class StudioAgent
{
    public required string AgentId { get; set; }
    public DateTime ClaimedAt { get; set; }

    /// <summary>The owner's emergency stop (D121).</summary>
    public bool Suspended { get; set; }

    /// <summary>Fingerprint of the secret last handed to kagent (D118).</summary>
    public string? DeployedFingerprint { get; set; }
}
