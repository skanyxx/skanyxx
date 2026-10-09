using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The owner's reset link for a person (D157): <c>201 {link, expiresAt, emailed}</c>; <c>link</c> is null once emailed.</summary>
internal sealed class IssuePasswordResetEndpoint(IMediator mediator) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Post("people/{id}/password-reset");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new IssuePasswordResetCommand(Caller.UserId(User)!, req.Id), ct);
        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.ResultAsync(outcome.ToHttp(i => new { link = i.Link, expiresAt = i.ExpiresAt, emailed = i.Emailed }));
    }
}
