using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Sources;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>
/// Install is stateless (P12): the source is read again, and proposed only when its digest is the one inspect showed,
/// so what the person reviewed is what the pull request carries. The proposal itself is <see cref="InstallPluginCommand"/>.
/// </summary>
internal sealed class InstallPluginSourceHandler(PluginLoader loader, IMediator mediator, ILogger<InstallPluginSourceHandler> logger)
    : IRequestHandler<InstallPluginSourceCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(InstallPluginSourceCommand command, CancellationToken ct)
    {
        var (user, request) = (command.User, command.Request);
        if (!PluginAccess.CanInstall(user, request.Source))
        {
            logger.LogWarning("Plugin install ({Kind}) refused for {ActorUserId}", request.Source, user.UserId);
            return Outcome<StudioResult>.Forbidden(PluginAccess.NotInstaller);
        }
        PluginDraft draft;
        try
        {
            var result = await loader.LoadAsync(request, command.Upload, PluginAccess.CanInspect(user, PluginSourceKinds.Git), ct);
            draft = result.Plugin ?? throw new PluginSourceException("The source is a marketplace: name the plugin to install.");
        }
        catch (PluginSourceException ex) when (ex.Forbidden)
        {
            logger.LogWarning("Plugin install (catalog entry pinned to git) refused for {ActorUserId}: {Reason}", user.UserId, ex.Message);
            return Outcome<StudioResult>.Forbidden(PluginAccess.NotInstaller);
        }
        catch (PluginSourceException ex) when (ex.Upstream)
        {
            logger.LogWarning("Plugin source ({Kind}) for {ActorUserId} could not be read: {Reason}", request.Source, user.UserId, ex.Message);
            return Outcome<StudioResult>.Unavailable(ex.Message);
        }
        catch (PluginSourceException ex)
        {
            throw PluginSourceFailure.Refused(ex);
        }
        if (draft.Source.Digest != request.Digest)
            return StudioFailure.Conflict<StudioResult>(
                "The source is not what was inspected (its digest changed); inspect it again and review what it adds.", "digest");
        return await mediator.Send(new InstallPluginCommand(user, draft), ct);
    }
}
