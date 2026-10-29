using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Contracts;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>Prices are the owner's alone; audited.</summary>
internal sealed class SetPriceHandler(QuotaStore store, IMediator mediator, TimeProvider clock) : IRequestHandler<SetPriceCommand, Outcome<PriceDto>>
{
    public async Task<Outcome<PriceDto>> Handle(SetPriceCommand command, CancellationToken ct)
    {
        if (!command.Viewer.IsOwner)
            return Outcome<PriceDto>.Forbidden("Only the owner sets model prices.");
        var previous = await store.SetPriceAsync(command.Model, command.PricePer1K, command.Viewer.UserId, clock.GetUtcNow(), ct);
        await mediator.Send(new RecordAuditCommand("quota.price.set", command.Viewer.UserId, command.Model, new
        {
            model = command.Model, pricePer1K = command.PricePer1K, previous, currency = QuotaPrice.Currency
        }), CancellationToken.None);
        return Outcome<PriceDto>.Ok(new PriceDto(command.Model, command.PricePer1K));
    }
}
