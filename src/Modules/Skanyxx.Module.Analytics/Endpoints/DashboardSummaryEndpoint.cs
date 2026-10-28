using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Features;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary><c>GET api/dashboard/summary</c>: the owner; a supervisor read-only, without per-person lists or the audit.</summary>
internal sealed class DashboardSummaryEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("summary");
        Group<DashboardGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync(Results.Ok(await mediator.Send(new DashboardSummaryQuery(AnalyticsViewer.From(User)), ct)));
}
