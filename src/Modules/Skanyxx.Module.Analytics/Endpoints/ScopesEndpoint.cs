using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Module.Analytics.Features;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary><c>GET api/analytics/scopes</c>: the drill-down the caller may open (just "Me" for most people).</summary>
internal sealed class ScopesEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("scopes");
        Group<AnalyticsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync(Results.Ok(await mediator.Send(new ScopeTreeQuery(AnalyticsViewer.From(User)), ct)));
}
