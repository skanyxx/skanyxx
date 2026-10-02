namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary><paramref name="CreatedBy"/>: the user id that issued the secret, so an offboarding can be followed by hand.</summary>
public sealed record AgentSecretStatus(string AgentId, bool HasSecret, DateTime? CreatedAt, bool ActsForUsers, string? CreatedBy);
