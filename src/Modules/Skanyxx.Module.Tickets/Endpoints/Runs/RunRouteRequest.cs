using FastEndpoints;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

public class RunRouteRequest
{
    [DontBind(Source.QueryParam)]
    public Guid Id { get; set; }
}
