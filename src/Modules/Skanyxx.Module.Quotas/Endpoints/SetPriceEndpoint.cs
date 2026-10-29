using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary><c>PUT api/quotas/prices/{namespace}/{name}</c>: the owner only.</summary>
internal sealed class SetPriceEndpoint(IMediator mediator) : Endpoint<SetPriceRequest>
{
    public override void Configure()
    {
        Put("prices/{namespace}/{name}");
        Group<QuotasGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(SetPriceRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new SetPriceCommand(QuotaViewer.From(User), $"{req.Namespace}/{req.Name}", req.PricePer1K), ct)).ToHttp());
}
