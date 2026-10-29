using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Http;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Quotas.Features;

namespace Skanyxx.Module.Quotas.Endpoints;

/// <summary><c>POST api/quotas/requests/{id}/approve|deny</c>: the owner any; a supervisor their members' (the handler checks).</summary>
internal sealed class DecideRequestEndpoint(IMediator mediator) : Endpoint<DecideRequest>
{
    public override void Configure()
    {
        Post("requests/{id}/{decision}");
        Group<QuotasGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(DecideRequest req, CancellationToken ct)
    {
        if (req.Decision is not ("approve" or "deny"))
        {
            await Send.ResultAsync(Results.Problem("A request is approved or denied.", statusCode: StatusCodes.Status400BadRequest));
            return;
        }
        await Send.ResultAsync((await mediator.Send(new DecideRequestCommand(QuotaViewer.From(User), req.Id, req.Decision == "approve"), ct)).ToHttp());
    }
}
