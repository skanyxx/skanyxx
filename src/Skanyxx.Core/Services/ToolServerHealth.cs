using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Core.Services;

/// <summary>
/// Whether an agent's MCP tool servers can give it tools, from kagent's tool server list (<c>GET /api/toolservers</c>:
/// <c>{ref, groupKind, discoveredTools}</c>), which the callers read anyway (D554). kagent's HTTP API carries no
/// RemoteMCPServer status, and Skanyxx has no Kubernetes credentials (D099), so the Accepted condition and its message
/// are out of reach: what shows is a server kagent does not list, and one it lists with no tool discovered. kagent keeps
/// the tools it discovered last when discovery fails later, so a server that worked once and broke since looks healthy
/// here; the failed turn's own reason names it (D553). A server labelled <c>kagent.dev/discovery=disabled</c> lists no
/// tools on purpose and shows as a problem too.
/// </summary>
public static class ToolServerHealth
{
    public const string NotListed = "kagent does not list this tool server, so the agent gets none of its tools.";

    public const string NoTools = "kagent discovered no tools on this tool server: it is unreachable or refused kagent. "
        + "Its RemoteMCPServer's Accepted condition says why; the Tool servers page has the fixes.";

    /// <summary>What anyone but builders and up reads: no server name, no address (D553).</summary>
    public const string ForEmployees = "A tool this agent uses is unavailable right now.";

    /// <summary>One server's problem, for anyone but builders and up.</summary>
    public const string Unavailable = "Unavailable right now.";

    /// <summary>kagent's tool server list for a status that can do without it: null (health unknown) when kagent did not answer.</summary>
    public static async Task<JsonArray?> ListOrNullAsync(KAgentApiClient kagent, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await kagent.GetToolServerListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "kagent did not list its tool servers: agents' tool health is unknown");
            return null;
        }
    }

    /// <param name="serverRefs">The agent's servers as <c>namespace/name</c>.</param>
    /// <param name="toolServers">kagent's list; the first entry of a repeated ref wins, as on the Tool servers page.</param>
    public static IReadOnlyList<ToolServerProblem> Problems(IEnumerable<string> serverRefs, JsonArray toolServers)
    {
        var listed = toolServers.OfType<JsonObject>()
            .Select(s => (Ref: s["ref"] is JsonValue v && v.TryGetValue<string>(out var r) ? r : null, Tools: (s["discoveredTools"] as JsonArray)?.Count ?? 0))
            .Where(s => s.Ref is not null)
            .DistinctBy(s => s.Ref)
            .ToDictionary(s => s.Ref!, s => s.Tools);
        return [.. serverRefs.Distinct()
            .Select(r => !listed.TryGetValue(r, out var tools) ? new ToolServerProblem(r, NotListed) : tools == 0 ? new ToolServerProblem(r, NoTools) : null)
            .OfType<ToolServerProblem>()];
    }

    /// <summary>One line for an agent's status, null when its servers are fine: every problem for builders and up, else <see cref="ForEmployees"/>.</summary>
    public static string? Summary(IReadOnlyList<ToolServerProblem> problems, bool internals) =>
        problems.Count == 0 ? null : internals ? string.Join(" ", problems.Select(p => $"{p.Server}: {p.Problem}")) : ForEmployees;
}
