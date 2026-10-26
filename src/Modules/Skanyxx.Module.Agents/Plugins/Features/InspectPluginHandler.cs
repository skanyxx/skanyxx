using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Agents.Plugins.Sources;
using Skanyxx.Module.Agents.Studio.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Features;

internal sealed class InspectPluginHandler(PluginLoader loader, ILogger<InspectPluginHandler> logger)
    : IRequestHandler<InspectPluginQuery, Outcome<PluginInspection>>
{
    public async Task<Outcome<PluginInspection>> Handle(InspectPluginQuery query, CancellationToken ct)
    {
        var (user, request) = (query.User, query.Request);
        if (!PluginAccess.CanInspect(user, request.Source))
        {
            logger.LogWarning("Plugin inspect ({Kind}) refused for {ActorUserId}", request.Source, user.UserId);
            return Outcome<PluginInspection>.Forbidden(PluginAccess.NotInspector);
        }
        try
        {
            var result = await loader.LoadAsync(request, query.Upload, PluginAccess.CanInspect(user, PluginSourceKinds.Git), ct);
            return Outcome<PluginInspection>.Ok(result.Marketplace is { } marketplace
                ? new PluginInspection(new PluginListing(marketplace.Name, marketplace.Description,
                    [.. marketplace.Entries.Select(e => new PluginListingEntry(e.Name, e.Description, e.Version, e.Problem is null ? request.Entry(e.Name) : null, e.Problem))]), null)
                : new PluginInspection(null, PluginReport.From(result.Plugin!)));
        }
        catch (PluginSourceException ex) when (ex.Forbidden)
        {
            logger.LogWarning("Plugin inspect (catalog entry pinned to git) refused for {ActorUserId}: {Reason}", user.UserId, ex.Message);
            return Outcome<PluginInspection>.Forbidden(PluginAccess.NotInspector);
        }
        catch (PluginSourceException ex) when (ex.Upstream)
        {
            logger.LogWarning("Plugin source ({Kind}) for {ActorUserId} could not be read: {Reason}", request.Source, user.UserId, ex.Message);
            return Outcome<PluginInspection>.Unavailable(ex.Message);
        }
        catch (PluginSourceException ex)
        {
            throw PluginSourceFailure.Refused(ex);
        }
    }
}
