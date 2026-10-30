namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>A configured price (<c>Analytics:Prices</c>), per million model tokens in and out; read-only here (D549).</summary>
public sealed record PriceDto(string Model, decimal InputPerMillion, decimal OutputPerMillion);
