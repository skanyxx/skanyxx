using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.AuditExplorer;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The explorer's hits, the owner's only (D152): a page, the total and the next page's cursor.</summary>
internal sealed class SearchAuditEndpoint(IMediator mediator, TimeProvider time) : Endpoint<SearchAuditRequest>
{
    public override void Configure()
    {
        Get("audit/search");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(SearchAuditRequest req, CancellationToken ct)
    {
        var after = req.AfterAt is { } at && req.AfterId is { } id ? new AuditCursor(at, id) : null;
        var query = new SearchAuditQuery(req.Filter(time.GetUtcNow()), req.Sort == "asc", req.Limit ?? 50, after);
        await Send.ResultAsync((await mediator.Send(query, ct)).ToHttp());
    }
}
