using FastEndpoints;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

public sealed class RunTaskRequest : TaskRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Image { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public List<string>? Command { get; set; }

    [DontBind(Source.QueryParam)]
    public Dictionary<string, string>? Env { get; set; }

    [DontBind(Source.QueryParam)]
    public List<WorkspaceMount>? Workspaces { get; set; }

    [DontBind(Source.QueryParam)]
    public SandboxResources? Resources { get; set; }
}
