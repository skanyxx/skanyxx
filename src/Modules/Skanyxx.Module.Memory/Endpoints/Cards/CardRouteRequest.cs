using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

/// <summary>
/// FastEndpoints binds the query string after the route and body, so without DontBind a
/// <c>?scope=</c> would silently retarget the request away from the path that is logged and routed.
/// </summary>
public class CardRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Scope { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public string Key { get; set; } = "";
}
