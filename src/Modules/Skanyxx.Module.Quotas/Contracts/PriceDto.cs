namespace Skanyxx.Module.Quotas.Contracts;

/// <summary>Null <see cref="PricePer1K"/>: the price was cleared.</summary>
public sealed record PriceDto(string Model, decimal? PricePer1K);
