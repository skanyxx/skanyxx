using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Endpoints.Cards;

/// <summary>
/// The full body as plain text, for the library (D183). Served as an inert document: text/plain, no sniffing, and a
/// sandbox CSP, so a body that happens to be HTML never runs on Skanyxx's origin.
/// </summary>
internal sealed class GetCardBodyEndpoint(IMediator mediator) : Endpoint<CardRouteRequest>
{
    public override void Configure()
    {
        Get("cards/{scope}/{key}/body");
        Group<MemoryGroup>();
    }

    public override async Task HandleAsync(CardRouteRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new GetCardBodyQuery(HumanCaller.From(User), req.Scope, req.Key), ct);
        if (outcome is not { Status: OutcomeStatus.Ok, Value: { } body })
        {
            await Send.ResultAsync(outcome.ToHttp());
            return;
        }

        HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
        HttpContext.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        // A private document: never kept by the browser or a shared cache.
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        HttpContext.Response.ContentLength = body.Length;
        await Send.ResultAsync(Results.Stream(body.Content, "text/plain; charset=utf-8"));
    }
}
