using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Features;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary><c>GET api/dashboard/health</c>: one tile per dependency, fixed texts only (no error body, no connection string).</summary>
internal sealed class DashboardHealthEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("health");
        Group<DashboardGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync(Results.Ok(await mediator.Send(new DashboardHealthQuery(), ct)));
}
