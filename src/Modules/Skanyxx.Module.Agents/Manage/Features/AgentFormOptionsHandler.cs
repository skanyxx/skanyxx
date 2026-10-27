using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>What the create/edit form offers: the same allow-lists <see cref="AgentFormRules"/> holds the form to.</summary>
internal sealed class AgentFormOptionsHandler(
    KAgentApiClient kagent, StudioReconciler reconciler, IOptions<StudioOptions> options, ILogger<AgentFormOptionsHandler> logger)
    : IRequestHandler<AgentFormOptionsQuery, Outcome<AgentFormOptions>>
{
    public async Task<Outcome<AgentFormOptions>> Handle(AgentFormOptionsQuery query, CancellationToken ct)
    {
        if (!AgentAccess.CanWrite(query.User))
            return Outcome<AgentFormOptions>.Forbidden(AgentAccess.NotWriter);
        var ns = options.Value.Namespace;
        try
        {
            var discovered = await reconciler.ToolServerToolsAsync(ct);
            return Outcome<AgentFormOptions>.Ok(new AgentFormOptions(ns,
                await AgentQueries.ModelsAsync(kagent, ns, ct),
                [.. options.Value.McpServers.Where(s => !s.StartsWith(StudioNames.MemoryServerPrefix, StringComparison.Ordinal)).Distinct()
                    .Select(s => new McpToolChoice(s, discovered.GetValueOrDefault($"{ns}/{s}") ?? []))],
                AgentQueries.Callable(await AgentQueries.AgentsAsync(kagent, ns, ct)),
                options.Value.SkillRegistries));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent form options: kagent did not answer");
            return Outcome<AgentFormOptions>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
