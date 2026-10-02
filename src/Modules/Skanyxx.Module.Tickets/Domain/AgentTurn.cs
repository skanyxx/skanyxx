namespace Skanyxx.Module.Tickets.Domain;

/// <summary>What one agent was asked and answered inside one stage attempt: the dataset row.</summary>
public sealed class AgentTurn
{
    public long Id { get; set; }
    public long StageRunId { get; set; }
    public required string AgentId { get; set; }
    public required string Agent { get; set; }
    public required string Prompt { get; set; }
    public required string Output { get; set; }
    public bool Degraded { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
}
