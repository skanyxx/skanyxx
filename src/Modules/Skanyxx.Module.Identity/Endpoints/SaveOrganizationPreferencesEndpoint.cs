using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The defaults everyone inherits where they have not chosen. Owner only.</summary>
internal sealed class SaveOrganizationPreferencesEndpoint(IMediator mediator) : Endpoint<PreferencesRequest>
{
    public override void Configure()
    {
        Put("org/preferences");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(PreferencesRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SaveOrganizationPreferencesCommand(Caller.UserId(User)!, req.ToSet()), ct)).ToHttp(s => s));
}
