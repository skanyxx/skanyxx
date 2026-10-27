using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

public sealed class SetCardStatusRequest : CardRouteRequest
{
    [DontBind(FastEndpoints.Source.QueryParam)]
    public int Version { get; set; }

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string Status { get; set; } = "";

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string? SupersededBy { get; set; }
}
