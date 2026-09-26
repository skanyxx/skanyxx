using FastEndpoints;

namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

public class TaskRouteRequest
{
    /// <summary>Route only: a <c>?name=</c> must not retarget the request.</summary>
    [DontBind(Source.QueryParam)]
    public string Name { get; set; } = "";
}
