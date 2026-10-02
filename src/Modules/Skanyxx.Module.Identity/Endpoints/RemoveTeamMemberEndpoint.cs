using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class RemoveTeamMemberEndpoint(IMediator mediator) : Endpoint<TeamMemberRequest>
{
    public override void Configure()
    {
        Delete("org/teams/{slug}/members/{userId}");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(TeamMemberRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new SetTeamMemberCommand(Caller.UserId(User)!, req.Slug, req.UserId, Member: false), ct)).ToHttp(t => t));
}
