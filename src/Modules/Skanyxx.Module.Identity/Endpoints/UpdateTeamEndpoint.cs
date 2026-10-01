using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Rename and/or move to another department: both fields are required (PUT replaces them).</summary>
internal sealed class UpdateTeamEndpoint(IMediator mediator) : Endpoint<TeamRequest>
{
    public override void Configure()
    {
        Put("org/teams/{slug}");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(TeamRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(
            new UpdateTeamCommand(Caller.UserId(User)!, req.Slug ?? "", req.Name?.Trim() ?? "", req.Department ?? ""), ct)).ToHttp(t => t));
}
