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
}
