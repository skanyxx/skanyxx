using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Reconcile;
using Skanyxx.Module.Agents.Studio.Templates;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// The gallery (D360): every template on the owner's default model. A cloud template is locked without the licence
/// (D364); with it, it brings the allow-listed cloud tool servers with the tools kagent discovered on them.
/// </summary>
internal sealed class StudioTemplatesHandler(
    StudioReconciler reconciler, IOptions<StudioOptions> options, IOptions<LicenceOptions> licence, ILogger<StudioTemplatesHandler> logger)
    : IRequestHandler<StudioTemplatesQuery, Outcome<IReadOnlyList<StudioTemplate>>>
{
    public async Task<Outcome<IReadOnlyList<StudioTemplate>>> Handle(StudioTemplatesQuery query, CancellationToken ct)
    {
        if (!query.User.CanEnter)
            return Outcome<IReadOnlyList<StudioTemplate>>.Forbidden(StudioFailure.NotStudio);
        var cloudTools = licence.Value.CloudTools ? await CloudToolsAsync(ct) : [];
        return Outcome<IReadOnlyList<StudioTemplate>>.Ok([.. StudioTemplates.All.Select(t => t with
        {
            Draft = t.Draft with { ModelConfig = options.Value.FactoryModelConfig, McpTools = t.CloudTools ? cloudTools : [] },
            Locked = t.CloudTools && !licence.Value.CloudTools
        })]);
    }

    private async Task<IReadOnlyList<McpToolChoice>> CloudToolsAsync(CancellationToken ct)
    {
        var o = options.Value;
        var servers = o.CloudToolServers.Where(o.McpServers.Contains).ToList();
        if (servers.Count == 0)
            return [];
        try
        {
            var discovered = await reconciler.ToolServerToolsAsync(ct);
            return [.. servers.Select(s => new McpToolChoice(s, discovered.GetValueOrDefault($"{o.Namespace}/{s}") ?? []))
                .Where(c => c.Tools.Count > 0 && CloudToolRules.IsPaid(c.Server, o))];
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning("Studio templates: kagent did not list the cloud tools ({Error})", ex.GetType().Name);
            return [];
        }
    }
}
