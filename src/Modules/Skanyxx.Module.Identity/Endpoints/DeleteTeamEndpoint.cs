using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary><c>DELETE org/teams/{slug}?to=&lt;team&gt;</c>: 409 while it has members and no <c>to</c> (D351).</summary>
internal sealed class DeleteTeamEndpoint(IMediator mediator) : Endpoint<DeleteOrgRequest>
{
    public override void Configure()
    {
        Delete("org/teams/{slug}");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(DeleteOrgRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new DeleteTeamCommand(Caller.UserId(User)!, req.Slug, Blank(req.To)), ct);
        if (outcome.Status == OutcomeStatus.Ok)
            await Send.NoContentAsync(ct);
        else
            await Send.ResultAsync(outcome.ToHttp());
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
