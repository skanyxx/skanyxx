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
/// What stands between the caller and using the Agents page (D312): kagent unreachable, no model, no MCP server for the
/// form, no Studio for a proposal. Empty when nothing is missing. kagent's own answer goes to the log, never here.
/// </summary>
internal sealed class AgentsSetupHandler(KAgentApiClient kagent, IOptions<StudioOptions> options, ILogger<AgentsSetupHandler> logger)
    : IRequestHandler<AgentsSetupQuery, Outcome<IReadOnlyList<SetupGuide>>>
{
    public async Task<Outcome<IReadOnlyList<SetupGuide>>> Handle(AgentsSetupQuery query, CancellationToken ct)
    {
        var admin = AgentAccess.CanWrite(query.User);
        IReadOnlyList<string> models;
        try
        {
            models = await AgentQueries.ModelsAsync(kagent, options.Value.Namespace, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agents setup: kagent did not answer");
            return Outcome<IReadOnlyList<SetupGuide>>.Ok([SetupGuides.KAgentUnreachable(admin)]);
        }
        var guides = new List<SetupGuide>();
        if (models.Count == 0)
            guides.Add(SetupGuides.NoModels(options.Value.Namespace, admin));
        if (admin && !options.Value.McpServers.Any(s => !s.StartsWith(StudioNames.MemoryServerPrefix, StringComparison.Ordinal)))
            guides.Add(SetupGuides.NoMcpServers());
        if (!admin && !options.Value.Configured)
            guides.Add(SetupGuides.StudioNotConfigured(admin));
        return Outcome<IReadOnlyList<SetupGuide>>.Ok(guides);
    }
}
