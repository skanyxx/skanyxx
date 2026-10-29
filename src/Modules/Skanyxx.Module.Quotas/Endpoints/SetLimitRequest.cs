using FastEndpoints;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary>Kind and id from the route, the limit from the body (null clears it).</summary>
public sealed class SetLimitRequest
{
    [DontBind(Source.QueryParam)]
    public string? Kind { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Id { get; set; }

    [DontBind(Source.QueryParam)]
    public long? MonthlyTokens { get; set; }
}
