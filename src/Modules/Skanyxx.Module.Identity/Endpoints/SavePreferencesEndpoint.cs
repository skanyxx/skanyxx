using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Preferences;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Replaces the caller's own choices; a field left out inherits the organization's default.</summary>
internal sealed class SavePreferencesEndpoint(IMediator mediator) : Endpoint<PreferencesRequest>
{
    public override void Configure()
    {
        Put("preferences");
        Group<MeGroup>();
    }

    public override async Task HandleAsync(PreferencesRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SavePreferencesCommand(Caller.UserId(User)!, req.ToSet()), ct)).ToHttp(v => v));
}
