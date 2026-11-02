namespace Skanyxx.Module.Investigate.Domain;

/// <summary>One step of one run: its own row, so parallel steps never write the same one.</summary>
public sealed class StepRun
{
    public Guid RunId { get; set; }

    public string StepId { get; set; } = "";

    public StepState State { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public string? Output { get; set; }

    public string? Error { get; set; }

    /// <summary>kagent's own words when it ended the step's task badly, cleaned (D556); only builders and up read it.</summary>
    public string? KagentReason { get; set; }
}
