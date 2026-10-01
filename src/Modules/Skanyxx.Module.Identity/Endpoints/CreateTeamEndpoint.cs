using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class CreateTeamEndpoint(IMediator mediator) : Endpoint<TeamRequest>
{
    public override void Configure()
    {
        Post("org/teams");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(TeamRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new CreateTeamCommand(Caller.UserId(User)!, req.Slug ?? "", req.Name?.Trim() ?? "", req.Department ?? ""), ct)).ToHttp(t => t));
}
