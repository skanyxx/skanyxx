using FastEndpoints;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Endpoints.Pipelines;

public sealed class SavePipelineRequest : PipelineRouteRequest
{
    [DontBind(Source.QueryParam)]
    public string Name { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public string Description { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public List<PipelineStage> Stages { get; set; } = [];
}
