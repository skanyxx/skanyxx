namespace Skanyxx.Module.Tickets.Contracts;

/// <summary>Which agent answered and whether it was a fallback. The full prompt and answer are in the dataset.</summary>
public sealed record AgentTurnDto(string AgentId, string Agent, bool Degraded, DateTime StartedAt, DateTime EndedAt);
