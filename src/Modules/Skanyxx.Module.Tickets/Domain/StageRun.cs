namespace Skanyxx.Module.Tickets.Domain;

/// <summary>One attempt at one stage. A loop adds attempts; earlier ones are never overwritten.</summary>
public sealed class StageRun
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public required string StageId { get; set; }
    public StageKind Kind { get; set; }
    public required string Title { get; set; }
    public int Attempt { get; set; }
    public StageState State { get; set; }
    public Verdict Verdict { get; set; }
    public required string Output { get; set; }

    /// <summary>No agent answered; <see cref="Output"/> is a deterministic fallback.</summary>
    public bool Degraded { get; set; }

    public List<string> Warnings { get; set; } = [];
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? DecidedBy { get; set; }

    /// <summary>A person's note at a gate; a rejection's note is shown to the stage that is re-run.</summary>
    public string? Note { get; set; }
    public List<AgentTurn> Turns { get; set; } = [];
}
