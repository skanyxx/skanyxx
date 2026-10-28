using MediatR;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Features;

internal sealed class ScopeTreeHandler(OrgReader orgs) : IRequestHandler<ScopeTreeQuery, IReadOnlyList<ScopeNode>>
{
    public async Task<IReadOnlyList<ScopeNode>> Handle(ScopeTreeQuery query, CancellationToken ct) =>
        ScopeResolver.Tree(query.Viewer, await orgs.ReadAsync(ct));
}
