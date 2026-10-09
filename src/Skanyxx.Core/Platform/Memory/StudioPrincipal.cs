using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Core.Platform.Memory;

/// <param name="Taken">
/// Memory holds a secret or grants for this name that the studio did not claim (the owner's agent, an external client),
/// or a secret that acts for users: the studio never takes it over (D117).
/// </param>
/// <param name="Claimed">The studio claimed this name in memory (D117) and has not given it back.</param>
/// <param name="Suspended">The owner's emergency stop (D121): no secret, no grants, not in kagent until resumed.</param>
/// <param name="SecretFingerprint">The live studio-issued secret's fingerprint; null when there is none (or a person rotated it).</param>
/// <param name="DeployedFingerprint">The fingerprint of the secret last handed to kagent (D118).</param>
public sealed record StudioPrincipal(
    string AgentId, bool Taken, bool Claimed, bool Suspended, string? SecretFingerprint, string? DeployedFingerprint, IReadOnlyList<StudioGrant> Grants)
{
    /// <summary>kagent holds the secret memory knows: nothing to issue.</summary>
    public bool SecretInPlace => SecretFingerprint is not null && SecretFingerprint == DeployedFingerprint;
}
