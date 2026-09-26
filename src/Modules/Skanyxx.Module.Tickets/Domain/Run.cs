namespace Skanyxx.Module.Tickets.Domain;

/// <summary>One pipeline over one ticket. Ticket and stages are snapshots: editing either later never rewrites a run.</summary>
public sealed class Run
{
    public Guid Id { get; set; }
    public required string TicketKey { get; set; }
    public required Ticket Ticket { get; set; }
    public required string PipelineId { get; set; }
    public required string PipelineName { get; set; }
    public List<PipelineStage> Stages { get; set; } = [];
    public RunState State { get; set; }

    /// <summary>Index into <see cref="Stages"/> of the stage to run (or awaiting a person).</summary>
    public int Cursor { get; set; }

    /// <summary>Stage id → times that stage has sent the run backwards.</summary>
    public Dictionary<string, int> Loops { get; set; } = [];

    /// <summary>Stage id being re-run → id of the stage that rejected it.</summary>
    public Dictionary<string, string> LoopedFrom { get; set; } = [];

    /// <summary>Why the run failed, when it was not a stage's own outcome (an internal error or a spent budget).</summary>
    public string? Error { get; set; }

    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }
    public List<StageRun> StageRuns { get; set; } = [];

    public PipelineStage CurrentStage => Stages[Cursor];
    public bool IsTerminal => State is RunState.Succeeded or RunState.Failed or RunState.Cancelled;
}
