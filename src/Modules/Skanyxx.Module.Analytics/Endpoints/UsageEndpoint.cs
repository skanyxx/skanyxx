using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Features;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary><c>GET api/analytics/usage</c>: anyone their own; the owner and supervisors wider, per <see cref="ScopeResolver"/>.</summary>
internal sealed class UsageEndpoint(IMediator mediator, TimeProvider clock) : Endpoint<UsageRequest>
{
    public override void Configure()
    {
        Get("usage");
        Group<AnalyticsGroup>();
    }

    public override async Task HandleAsync(UsageRequest req, CancellationToken ct)
    {
        var viewer = AnalyticsViewer.From(User);
        var (scope, range, error) = req.Parse(viewer, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        if (error is not null)
        {
            await Send.ResultAsync(Results.Problem(error, statusCode: StatusCodes.Status400BadRequest));
            return;
        }
        await Send.ResultAsync((await mediator.Send(new UsageReportQuery(viewer, scope!, range!, req.Compare), ct)).ToHttp());
    }
}
