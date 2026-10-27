using FastEndpoints;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal class SessionRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Id { get; set; } = "";
}
