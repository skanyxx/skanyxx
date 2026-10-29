using FastEndpoints;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary>The ModelConfig from the route, the price per 1,000 tokens from the body (null clears it).</summary>
public sealed class SetPriceRequest
{
    [DontBind(Source.QueryParam)]
    public string? Namespace { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Name { get; set; }

    [DontBind(Source.QueryParam)]
    public decimal? PricePer1K { get; set; }
}
