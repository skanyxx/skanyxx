namespace Skanyxx.Module.Investigate.Domain;

/// <summary>
/// One investigation: the workflow's steps as they were when it started, the input, and how it went. The worker
/// that runs it stamps <see cref="HeartbeatAt"/>; a running run whose heartbeat stops (its replica died) is failed.
/// </summary>
public sealed class InvestigationRun
{
    public Guid Id { get; set; }

    public Guid WorkflowId { get; set; }

    public string WorkflowName { get; set; } = "";

    public List<WorkflowStep> Steps { get; set; } = [];

    public string Input { get; set; } = "";

    public RunState State { get; set; }

    public string? Failure { get; set; }

    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public DateTime? HeartbeatAt { get; set; }

    public bool CancelRequested { get; set; }

    public string? CardScope { get; set; }

    public string? CardKey { get; set; }

    public List<StepRun> StepRuns { get; set; } = [];

    public bool IsFinished => State is RunState.Succeeded or RunState.Failed or RunState.Cancelled;
}
