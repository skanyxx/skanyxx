using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary><c>GET api/quotas/me</c>: the caller's own month and requests. Everyone.</summary>
internal sealed class MyQuotaEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("me");
        Group<QuotasGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync(Results.Ok(await mediator.Send(new MyQuotaQuery(QuotaViewer.From(User)), ct)));
}
