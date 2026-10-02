using FastEndpoints;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

public sealed class StartRunRequest
{
    [DontBind(Source.QueryParam)]
    public string TicketKey { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public string PipelineId { get; set; } = "";
}
