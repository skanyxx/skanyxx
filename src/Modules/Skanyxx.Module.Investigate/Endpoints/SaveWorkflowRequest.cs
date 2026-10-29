using FastEndpoints;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Endpoints;

public sealed class SaveWorkflowRequest : IdRequest
{
    [DontBind(Source.QueryParam)]
    public string Name { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public string Description { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public string? SharedTeam { get; set; }

    [DontBind(Source.QueryParam)]
    public List<WorkflowStep> Steps { get; set; } = [];

    /// <summary>Owner or supervisor only (D372): steps that read an earlier answer may call a write-capable agent.</summary>
    [DontBind(Source.QueryParam)]
    public bool AllowChainedWrites { get; set; }
}
