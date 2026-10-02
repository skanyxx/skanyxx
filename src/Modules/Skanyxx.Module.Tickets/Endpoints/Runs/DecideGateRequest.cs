using FastEndpoints;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Endpoints.Runs;

public sealed class DecideGateRequest : RunRouteRequest
{
    /// <summary>Nullable on purpose: an empty body must be a 400, not an approval.</summary>
    [DontBind(Source.QueryParam)]
    public Decision? Decision { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Note { get; set; }
}
