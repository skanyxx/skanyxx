using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary><c>POST api/quotas/requests</c>: anyone asks for more for themselves.</summary>
internal sealed class RequestMoreEndpoint(IMediator mediator) : Endpoint<RequestMoreRequest>
{
    public override void Configure()
    {
        Post("requests");
        Group<QuotasGroup>();
    }

    public override async Task HandleAsync(RequestMoreRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new RequestMoreCommand(QuotaViewer.From(User), req.Amount, req.Reason ?? ""), ct)).ToHttp());
}
