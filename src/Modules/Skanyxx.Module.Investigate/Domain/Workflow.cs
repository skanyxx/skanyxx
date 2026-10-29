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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
