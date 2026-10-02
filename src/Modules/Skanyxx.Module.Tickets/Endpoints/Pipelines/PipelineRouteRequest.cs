using FastEndpoints;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

public class PipelineRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Id { get; set; } = "";
}
