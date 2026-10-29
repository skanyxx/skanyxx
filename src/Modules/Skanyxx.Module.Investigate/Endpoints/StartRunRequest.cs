using FastEndpoints;

namespace Skanyxx.Module.Investigate.Endpoints;

public sealed class StartRunRequest
{
    [DontBind(Source.QueryParam)]
    public Guid WorkflowId { get; set; }

    [DontBind(Source.QueryParam)]
    public string Input { get; set; } = "";
}
