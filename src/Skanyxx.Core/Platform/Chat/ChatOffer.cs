using System.Text.Json.Nodes;
using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// Which kagent agents people may talk to (D557): every Agent kagent lists, wherever it came from (Studio, the Agents
/// page, the kagent UI, kubectl, Helm), except Skanyxx's own machinery, an agent that names another agent's memory
/// server, and a suspended agent (D257). Chat's picker and every chat route, Investigate's steps and the Agents page's
/// "In Chat" all ask this one rule, so they never disagree. The <c>skanyxx.dev/merged</c> label still means "came
/// through review" (Studio), but no longer gates Chat. An agent is <c>namespace/name</c>: two may share a name.
/// </summary>
public static class ChatOffer
{
    /// <summary>On a Studio preview agent: the pull request it previews (D030). It is chatted with in Studio only.</summary>
    public const string PreviewLabel = "skanyxx.dev/preview";

    /// <summary>On an agent Skanyxx runs for itself, such as the Studio factory (D5).</summary>
    public const string RoleLabel = "skanyxx.dev/role";

    /// <summary>
    /// Kubernetes' "part of" label. A value starting with <c>skanyxx</c> marks Skanyxx's own objects (the Tickets stage
    /// agents carry <c>skanyxx-ticket-flow</c>); of those, only <see cref="SeedPartOf"/> is meant for people.
    /// </summary>
    public const string PartOfLabel = "app.kubernetes.io/part-of";

    /// <summary>The seed's <see cref="PartOfLabel"/>: Skanyxx installs it, and it is the agent people talk to first.</summary>
    public const string SeedPartOf = "skanyxx-memory";

    /// <summary>A memory server is <c>skanyxx-memory-&lt;agent&gt;</c>; its secret is that agent's identity in memory (D080).</summary>
    public const string MemoryServerPrefix = "skanyxx-memory-";

    /// <summary>
    /// Whether Chat offers this agent while it is not suspended (suspension is <see cref="Offers"/>'s).
    /// </summary>
    /// <param name="serverRefs">Its MCP servers as <c>namespace/name</c> (<see cref="Agent.McpServerRefs"/>).</param>
    public static bool IsChatAgent(string ns, string name, IReadOnlyDictionary<string, string> labels, IEnumerable<string> serverRefs) =>
        !IsSkanyxxOwn(name, labels) && !BorrowsMemory(ns, name, serverRefs);

    public static bool IsChatAgent(Agent agent) => IsChatAgent(agent.Namespace, agent.Name, agent.Labels, agent.McpServerRefs);

    /// <summary>The same for an Agent CR as kagent's controller API returns it.</summary>
    public static bool IsChatAgent(JsonObject agent, IReadOnlyDictionary<string, string> labels) =>
        IsChatAgent(agent["metadata"]?["namespace"]?.GetValue<string>() ?? "", agent["metadata"]?["name"]?.GetValue<string>() ?? "",
            labels, ToolServerHealth.ServerRefs(agent));

    /// <summary>Whether Chat offers it now: a chat agent that is not suspended.</summary>
    public static bool Offers(Agent agent) => !agent.Suspended && IsChatAgent(agent);

    /// <summary>
    /// Skanyxx's own machinery, failing closed: a preview, a role, a <c>skanyxx</c> "part of" label other than the seed's,
    /// or a name Skanyxx reserves for its objects (<c>skanyxx-*</c>, <c>preview-*</c>).
    /// </summary>
    private static bool IsSkanyxxOwn(string name, IReadOnlyDictionary<string, string> labels) =>
        labels.ContainsKey(PreviewLabel) || labels.ContainsKey(RoleLabel)
        || labels.GetValueOrDefault(PartOfLabel) is { } partOf && partOf != SeedPartOf && partOf.StartsWith("skanyxx", StringComparison.Ordinal)
        || name.StartsWith("skanyxx-", StringComparison.Ordinal) || name.StartsWith("preview-", StringComparison.Ordinal);

    /// <summary>
    /// An agent naming a memory server other than its own (<c>namespace/skanyxx-memory-&lt;name&gt;</c>) would act in memory
    /// as that agent, and for the person when that server forwards x-user-id (D084), without any review.
    /// </summary>
    private static bool BorrowsMemory(string ns, string name, IEnumerable<string> serverRefs) =>
        serverRefs.Any(r => r.Split('/') is [_, var server] && server.StartsWith(MemoryServerPrefix, StringComparison.Ordinal)
            && r != $"{ns}/{MemoryServerPrefix}{name}");
}
