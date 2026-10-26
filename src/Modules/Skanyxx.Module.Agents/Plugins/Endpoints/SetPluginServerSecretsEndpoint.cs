using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>The owner's header values for a merged plugin server; the answer says how many were stored, never what.</summary>
internal sealed class SetPluginServerSecretsEndpoint(IMediator mediator) : Endpoint<PluginServerSecretsRequest>
{
    public override void Configure()
    {
        Put("servers/{server}/secrets");
        Group<PluginsGroup>();
        Roles(SkanyxxRoles.Owner);
    }

    public override async Task HandleAsync(PluginServerSecretsRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SetPluginServerSecretsCommand(StudioUser.From(User), req.Server, req.Values ?? []), ct)).ToHttp());
}
