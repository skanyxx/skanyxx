using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.AuditExplorer;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Hits over time, stacked by outcome, the owner's only (D152).</summary>
internal sealed class AuditHistogramEndpoint(IMediator mediator, TimeProvider time) : Endpoint<AuditQueryRequest>
{
    public override void Configure()
    {
        Get("audit/histogram");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(AuditQueryRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new AuditHistogramQuery(req.Filter(time.GetUtcNow())), ct)).ToHttp());
}
