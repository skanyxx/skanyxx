using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;

namespace Skanyxx.Module.Identity.Endpoints;

internal sealed class SaveEntraSettingsEndpoint(IMediator mediator) : Endpoint<EntraSettingsRequest>
{
    public override void Configure()
    {
        Put("entra/settings");
        Group<IdentityGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(EntraSettingsRequest req, CancellationToken ct)
    {
        var command = new SaveEntraSettingsCommand(Caller.UserId(User)!, req.Enabled, req.TenantId?.Trim() ?? "", req.ClientId?.Trim() ?? "",
            req.ClientSecret, [.. (req.Groups ?? []).Select(g => new EntraGroupMapDto(g.GroupId?.Trim() ?? "", g.Label, g.Roles ?? [], g.Teams ?? []))]);
        await Send.ResultAsync((await mediator.Send(command, ct)).ToHttp(s => s));
    }
}
