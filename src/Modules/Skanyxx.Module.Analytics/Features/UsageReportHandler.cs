using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;
using Skanyxx.Module.Analytics.Usage;

namespace Skanyxx.Module.Analytics.Features;

internal sealed class UsageReportHandler(OrgReader orgs, UsageStore store, UsageReportBuilder builder)
    : IRequestHandler<UsageReportQuery, Outcome<UsageReport>>
{
    public async Task<Outcome<UsageReport>> Handle(UsageReportQuery query, CancellationToken ct)
    {
        var org = await orgs.ReadAsync(ct);
        var resolved = ScopeResolver.Resolve(query.Viewer, query.Scope, org);
        if (resolved.Value is not { } scope)
            return new Outcome<UsageReport>(resolved.Status, Message: resolved.Message);

        var rows = await store.ReadAsync(query.Range.From, query.Range.To, scope.Users, ct);
        var previous = query.Compare ? await store.ReadAsync(query.Range.Previous.From, query.Range.Previous.To, scope.Users, ct) : null;
        return Outcome<UsageReport>.Ok(builder.Build(scope, query.Range, rows, previous, org));
    }
}
