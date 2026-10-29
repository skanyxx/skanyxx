using FastEndpoints;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary>Route only: the request id and <c>approve</c> or <c>deny</c>.</summary>
public sealed class DecideRequest
{
    [DontBind(Source.QueryParam)]
    public long Id { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Decision { get; set; }
}
