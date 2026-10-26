using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Features;

namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>
/// Install = one studio pull request (<c>201</c> with its number); nothing reaches kagent until a supervisor merges it.
/// <c>409</c> <c>digest</c> when the source changed since inspect. Catalog: builders and the owner; git: the owner.
/// </summary>
internal sealed class InstallPluginEndpoint(IMediator mediator) : Endpoint<PluginSourceRequest>
{
    public override void Configure()
    {
        Post("install");
        Group<PluginsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Builder);
    }

    public override async Task HandleAsync(PluginSourceRequest req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new InstallPluginSourceCommand(StudioUser.From(User), req, null), ct)).ToHttp());
}
