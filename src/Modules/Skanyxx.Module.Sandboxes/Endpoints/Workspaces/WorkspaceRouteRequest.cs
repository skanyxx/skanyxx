using FastEndpoints;

namespace Skanyxx.Module.Sandboxes.Endpoints.Workspaces;

public class WorkspaceRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Name { get; set; } = "";
}
