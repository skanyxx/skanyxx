using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class AddTeamMemberEndpoint(IMediator mediator) : Endpoint<TeamMemberRequest>
{
    public override void Configure()
    {
        Put("org/teams/{slug}/members/{userId}");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(TeamMemberRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new SetTeamMemberCommand(Caller.UserId(User)!, req.Slug, req.UserId, Member: true), ct)).ToHttp(t => t));
}
