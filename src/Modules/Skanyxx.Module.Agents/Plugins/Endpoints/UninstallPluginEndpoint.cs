using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>A pull request removing the plugin (builders and the owner); after the merge its agents, then its servers, leave kagent.</summary>
internal sealed class UninstallPluginEndpoint(IMediator mediator) : Endpoint<PluginRoute>
{
    public override void Configure()
    {
        Post("{name}/uninstall");
        Group<PluginsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Builder);
    }

    public override async Task HandleAsync(PluginRoute req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new UninstallPluginCommand(StudioUser.From(User), req.Name), ct)).ToHttp());
}
