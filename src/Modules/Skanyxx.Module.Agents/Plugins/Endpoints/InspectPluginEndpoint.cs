using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Features;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>
/// A marketplace listing, or what one plugin would add (agents, servers, header names and templates, notes) and its
/// digest. Writes nothing. Git sources are the owner's (403 before any download); a refused source is a 400 with the
/// reason, an unreachable git host a 502.
/// </summary>
internal sealed class InspectPluginEndpoint(IMediator mediator) : Endpoint<PluginSourceRequest>
{
    public override void Configure()
    {
        Post("inspect");
        Group<PluginsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder);
    }

    public override async Task HandleAsync(PluginSourceRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new InspectPluginQuery(StudioUser.From(User), req, null), ct)).ToHttp());
}
