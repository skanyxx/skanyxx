using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Saves the given installation settings (D350); answers the keys that changed. Owner only, audited.</summary>
internal sealed class SaveSettingsEndpoint(IMediator mediator) : Endpoint<SettingsRequest>
{
    public override void Configure()
    {
        Put("settings");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(SettingsRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SaveInstallationSettingsCommand(Caller.UserId(User)!, req.Values), ct)).ToHttp());
}
