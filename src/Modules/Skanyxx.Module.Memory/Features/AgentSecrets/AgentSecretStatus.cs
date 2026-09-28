namespace Skanyxx.Module.Memory.Features.AgentSecrets;

public sealed record AgentSecretStatus(string AgentId, bool HasSecret, DateTime? CreatedAt, bool ActsForUsers);
