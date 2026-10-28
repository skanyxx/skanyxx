using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;
using Skanyxx.Module.Analytics.Usage;

namespace Skanyxx.Module.Analytics.Features;

internal sealed class ExportUsageHandler(OrgReader orgs, UsageStore store, CostCalculator cost, IMediator mediator)
    : IRequestHandler<ExportUsageQuery, Outcome<string>>
{
    public async Task<Outcome<string>> Handle(ExportUsageQuery query, CancellationToken ct)
    {
        if (!query.Viewer.IsOwner)
            return Outcome<string>.Forbidden("Only the owner exports analytics.");
        var org = await orgs.ReadAsync(ct);
        var resolved = ScopeResolver.Resolve(query.Viewer, query.Scope, org);
        if (resolved.Value is not { } scope)
            return new Outcome<string>(resolved.Status, Message: resolved.Message);

        var rows = await store.ReadAsync(query.Range.From, query.Range.To, scope.Users, ct);
        await mediator.Send(new RecordAuditCommand("analytics.exported", query.Viewer.UserId, scope.Scope.Id, new
        {
            scope = scope.Scope.Kind.ToString().ToLowerInvariant(),
            from = query.Range.From.ToString("yyyy-MM-dd"),
            to = query.Range.To.ToString("yyyy-MM-dd"),
            rows = rows.Count
        }), ct);
        return Outcome<string>.Ok(UsageCsv.Write(rows, org, cost));
    }
}
