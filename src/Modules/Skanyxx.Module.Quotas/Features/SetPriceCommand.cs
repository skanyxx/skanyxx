using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Contracts;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Sets (null: clears) the price per 1,000 tokens on a ModelConfig (<c>namespace/name</c>).</summary>
public sealed record SetPriceCommand(QuotaViewer Viewer, string Model, decimal? PricePer1K) : IRequest<Outcome<PriceDto>>;
