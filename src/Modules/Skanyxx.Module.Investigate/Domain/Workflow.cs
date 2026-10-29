namespace Skanyxx.Module.Investigate.Domain;

/// <summary>
/// A named tree of steps that investigates an input. Its top-level steps run one after another. The owner and
/// supervisors edit it; members of <see cref="SharedTeam"/> may see, run and duplicate it.
/// </summary>
public sealed class Workflow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string OwnerId { get; set; } = "";

    public string? SharedTeam { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];

    /// <summary>
    /// Set by the owner or a supervisor (audited, D372): steps that read an earlier answer may call an agent that can
    /// write. Off, such a workflow is refused on save and on start.
    /// </summary>
    public bool AllowChainedWrites { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
