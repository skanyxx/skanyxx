using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Features.AuditExplorer;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Sign-ins over time and admin changes by kind, the owner's only (D152).</summary>
internal sealed class AuditAnalyticsEndpoint(IMediator mediator, TimeProvider time) : Endpoint<AuditQueryRequest>
{
    public override void Configure()
    {
        Get("audit/analytics");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(AuditQueryRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new AuditAnalyticsQuery(req.Filter(time.GetUtcNow())), ct)).ToHttp());
}
