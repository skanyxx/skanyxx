using System.Text;
using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Features;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary><c>GET api/analytics/export</c>: the owner's CSV, same parameters as usage (no compare). Audited.</summary>
internal sealed class ExportEndpoint(IMediator mediator, TimeProvider clock) : Endpoint<UsageRequest>
{
    public override void Configure()
    {
        Get("export");
        Group<AnalyticsGroup>();
        Roles(SkanyxxRoles.Owner);
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
        var outcome = await mediator.Send(new ExportUsageQuery(viewer, scope!, range!), ct);
        if (outcome.Status != OutcomeStatus.Ok)
        {
            await Send.ResultAsync(outcome.ToHttp());
            return;
        }
        var name = $"skanyxx-usage-{scope!.Kind.ToString().ToLowerInvariant()}-{range!.From:yyyyMMdd}-{range.To:yyyyMMdd}.csv";
        await Send.ResultAsync(Results.File(Encoding.UTF8.GetBytes(outcome.Value!), "text/csv; charset=utf-8", name));
    }
}
