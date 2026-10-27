using FastEndpoints;

namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class TicketRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Key { get; set; } = "";
}
