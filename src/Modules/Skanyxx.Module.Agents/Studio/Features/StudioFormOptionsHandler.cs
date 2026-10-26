using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// What the form offers: the owner's models (D099), the MCP servers an agent may use with their tools — the allow-list
/// and every plugin server merged in main (P7) — and the scopes.
/// </summary>
internal sealed class StudioFormOptionsHandler(
    KAgentApiClient kagent, StudioReconciler reconciler, IMediator mediator, IOptions<StudioOptions> options, ILogger<StudioFormOptionsHandler> logger)
    : IRequestHandler<StudioFormOptionsQuery, Outcome<StudioFormOptions>>
{
    public async Task<Outcome<StudioFormOptions>> Handle(StudioFormOptionsQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<StudioFormOptions>.Forbidden(StudioFailure.NotStudio);
        var ns = options.Value.Namespace;
        IReadOnlyList<string> models;
        List<McpToolChoice> servers;
        try
        {
            models = [.. (await kagent.GetModelConfigRefsAsync(ct)).Where(r => r.StartsWith(ns + "/", StringComparison.Ordinal)).Select(r => r[(ns.Length + 1)..])];
            var discovered = await reconciler.ToolServerToolsAsync(ct);
            var pluginServers = options.Value.Configured ? await PluginServersAsync(ct) : [];
            servers = [.. options.Value.McpServers.Concat(pluginServers.Order(StringComparer.Ordinal)).Distinct()
                .Select(s => new McpToolChoice(s, discovered.GetValueOrDefault($"{ns}/{s}") ?? []))];
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio form options: kagent did not answer");
            return Outcome<StudioFormOptions>.Unavailable(StudioFailure.Unavailable);
        }
        var teams = (await mediator.Send(new ListTeamsQuery(), ct)).Value ?? [];
        var departments = (await mediator.Send(new ListDepartmentsQuery(), ct)).Value ?? [];
        return Outcome<StudioFormOptions>.Ok(new StudioFormOptions(models, servers,
            ["company", .. teams.Select(t => $"team:{t.Slug}"), .. departments.Select(d => $"department:{d.Slug}")]));
    }

    /// <summary>The repo guard's refusal is the studio page's to show; the form then offers the allow-list alone.</summary>
    private async Task<IReadOnlyCollection<string>> PluginServersAsync(CancellationToken ct)
    {
        try
        {
            return (await reconciler.MainPluginServersAsync(ct)).Keys;
        }
        catch (StudioApplyException ex)
        {
            logger.LogWarning("Studio form options: plugin servers left out ({Problem})", ex.Message);
            return [];
        }
    }
}
