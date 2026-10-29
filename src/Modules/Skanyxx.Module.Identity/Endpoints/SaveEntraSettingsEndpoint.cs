using FastEndpoints;
using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class SaveEntraSettingsEndpoint(IMediator mediator, IOptionsMonitor<IdentityModuleOptions> options) : Endpoint<EntraSettingsRequest>
{
    public override void Configure()
    {
        Put("entra/settings");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(EntraSettingsRequest req, CancellationToken ct)
    {
        if (!options.CurrentValue.EntraAvailable)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        var command = new SaveEntraSettingsCommand(Caller.UserId(User)!, req.Enabled, req.TenantId?.Trim() ?? "", req.ClientId?.Trim() ?? "",
            req.ClientSecret, [.. (req.Groups ?? []).Select(g => new EntraGroupMapDto(g.GroupId?.Trim() ?? "", g.Label, g.Roles ?? [], g.Teams ?? []))]);
        await Send.ResultAsync((await mediator.Send(command, ct)).ToHttp(s => s));
    }
}
