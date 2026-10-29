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
}
