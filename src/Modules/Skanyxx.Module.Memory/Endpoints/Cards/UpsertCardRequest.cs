using FastEndpoints;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

public sealed class UpsertCardRequest : CardRouteRequest
{
    [DontBind(FastEndpoints.Source.QueryParam)]
    public int Version { get; set; }

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string Type { get; set; } = "";

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string What { get; set; } = "";

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string Why { get; set; } = "";

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string? Body { get; set; }

    [DontBind(FastEndpoints.Source.QueryParam)]
    public string? Source { get; set; }

    /// <summary>Null keeps the card's.</summary>
    [DontBind(FastEndpoints.Source.QueryParam)]
    public List<string>? Tickets { get; set; }

    [DontBind(FastEndpoints.Source.QueryParam)]
    public List<string>? Links { get; set; }

    [DontBind(FastEndpoints.Source.QueryParam)]
    public List<string>? Keywords { get; set; }
}
