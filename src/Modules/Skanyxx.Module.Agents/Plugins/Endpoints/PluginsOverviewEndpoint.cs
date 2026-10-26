using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>Installed plugins, their servers' state and header names (never a value), their agents, and open plugin proposals.</summary>
internal sealed class PluginsOverviewEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/" + PluginsGroup.Prefix);
        PluginsGroup.Apply(Definition, PluginsGroup.MaxBodyBytes);
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new PluginsOverviewQuery(StudioUser.From(User)), ct)).ToHttp());
}
