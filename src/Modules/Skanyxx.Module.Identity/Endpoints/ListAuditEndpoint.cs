using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The identity audit (D152), the owner's only; newest first.</summary>
internal sealed class ListAuditEndpoint(IMediator mediator) : Endpoint<ListAuditRequest>
{
    public override void Configure()
    {
        Get("audit");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(ListAuditRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListAuditQuery(req.Before, req.Limit ?? 50, req.Action, req.UserId), ct)).ToHttp(a => a));
}
