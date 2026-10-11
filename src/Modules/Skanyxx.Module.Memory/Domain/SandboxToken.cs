namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// D076: the one live memory token of a sandbox activation, keyed by its memory agent id. A token is accepted only while
/// this row holds its hash, so issuing again replaces it and stopping the sandbox deletes it (revocation is immediate,
/// not at expiry). Only the SHA-256 is stored.
/// </summary>
public sealed class SandboxToken
{
    public required string AgentId { get; set; }
    public required string SandboxName { get; set; }
    public required string OwnerId { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
