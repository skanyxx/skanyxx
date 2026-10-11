using FastEndpoints;

namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

public class SandboxRouteRequest
{
    /// <summary>Route only: a <c>?name=</c> must not retarget the request.</summary>
    [DontBind(Source.QueryParam)]
    public string Name { get; set; } = "";
}
