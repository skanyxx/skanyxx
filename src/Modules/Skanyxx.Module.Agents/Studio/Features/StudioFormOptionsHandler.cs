using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>What the form offers: the owner's models (D099), the allow-listed MCP servers with their tools, the scopes.</summary>
internal sealed class StudioFormOptionsHandler(KAgentApiClient kagent, IMediator mediator, IOptions<StudioOptions> options, ILogger<StudioFormOptionsHandler> logger)
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
            // kagent's list may repeat a ref or miss one (m14): the first of each wins, a nameless one is dropped.
            var discovered = (await kagent.GetToolServerListAsync(ct))
                .Select(s => (Ref: Text(s?["ref"]), Tools: (s?["discoveredTools"] as JsonArray ?? []).Select(t => Text(t?["name"])).OfType<string>().ToList()))
                .Where(s => s.Ref is not null)
                .GroupBy(s => s.Ref!)
                .ToDictionary(g => g.Key, g => g.First().Tools);
            servers = [.. options.Value.McpServers.Select(s => new McpToolChoice(s, discovered.GetValueOrDefault($"{ns}/{s}") ?? []))];
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

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
