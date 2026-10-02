using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

public sealed class LiftCardRequest : CardRouteRequest
{
    [DontBind(FastEndpoints.Source.QueryParam)]
    public string TargetScope { get; set; } = "";
}
