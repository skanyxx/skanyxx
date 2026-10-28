using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// What the create/edit form offers: the same allow-lists <see cref="AgentFormRules"/> holds the form to, with kagent's
/// tool descriptions. Everyone with a role gets it (D312): for supervisors the form saves, for anyone else it proposes.
/// </summary>
internal sealed class AgentFormOptionsHandler(KAgentApiClient kagent, IOptions<StudioOptions> options, ILogger<AgentFormOptionsHandler> logger)
    : IRequestHandler<AgentFormOptionsQuery, Outcome<AgentFormOptions>>
{
    public async Task<Outcome<AgentFormOptions>> Handle(AgentFormOptionsQuery query, CancellationToken ct)
    {
        var ns = options.Value.Namespace;
        try
        {
            var catalog = await AgentQueries.ToolCatalogAsync(kagent, ct);
            var direct = AgentAccess.CanWrite(query.User);
            return Outcome<AgentFormOptions>.Ok(new AgentFormOptions(ns,
                await AgentQueries.ModelsAsync(kagent, ns, ct),
                [.. options.Value.McpServers.Where(s => !s.StartsWith(StudioNames.MemoryServerPrefix, StringComparison.Ordinal)).Distinct()
                    .Select(s => new ServerTools(s, catalog.GetValueOrDefault($"{ns}/{s}") ?? []))],
                // Studio proposals never carry agent tools (AgentDraft has none).
                direct ? AgentQueries.Callable(await AgentQueries.AgentsAsync(kagent, ns, ct)) : [],
                options.Value.SkillRegistries,
                direct ? "direct" : "proposal",
                options.Value.Configured));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent form options: kagent did not answer");
            return Outcome<AgentFormOptions>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
