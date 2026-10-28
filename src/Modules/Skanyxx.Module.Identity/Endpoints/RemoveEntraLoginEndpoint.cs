using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class RemoveEntraLoginEndpoint(IMediator mediator, IOptions<IdentityModuleOptions> options) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Delete("people/{id}/entra-login");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        if (!options.Value.EntraAvailable)
            await Send.NotFoundAsync(ct);
        else
            await Send.ResultAsync((await mediator.Send(new RemoveEntraLoginCommand(Caller.UserId(User)!, req.Id), ct)).ToHttp(p => p));
    }
}
