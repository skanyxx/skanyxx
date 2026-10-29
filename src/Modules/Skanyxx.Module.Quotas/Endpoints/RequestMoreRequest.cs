using FastEndpoints;

namespace Skanyxx.Module.Quotas.Endpoints;

public sealed class RequestMoreRequest
{
    [DontBind(Source.QueryParam)]
    public long Amount { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Reason { get; set; }
}
