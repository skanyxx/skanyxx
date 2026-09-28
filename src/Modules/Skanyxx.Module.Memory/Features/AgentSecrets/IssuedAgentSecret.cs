namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary>The only response that ever carries the plaintext secret; it is not stored anywhere.</summary>
public sealed record IssuedAgentSecret(string AgentId, string Secret, DateTime CreatedAt, bool ActsForUsers);
