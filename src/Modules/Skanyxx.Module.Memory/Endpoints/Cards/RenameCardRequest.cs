using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

public sealed class RenameCardRequest : CardRouteRequest
{
    [DontBind(FastEndpoints.Source.QueryParam)]
    public string NewKey { get; set; } = "";

    [DontBind(FastEndpoints.Source.QueryParam)]
    public int Version { get; set; }
}
