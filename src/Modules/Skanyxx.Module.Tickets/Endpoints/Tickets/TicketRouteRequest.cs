using FastEndpoints;

namespace Skanyxx.Module.Tickets.Endpoints.Tickets;

public sealed class TicketRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Key { get; set; } = "";
}
