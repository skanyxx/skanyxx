using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>
/// The agents people may talk to: every agent kagent lists except Skanyxx's own machinery and suspended ones
/// (<see cref="ChatOffer"/>, D557; it replaced the <c>skanyxx.dev/merged</c> gate of D4/D100). An agent is
/// <c>namespace/name</c>: two may share a name. Every chat route asks this list, so an agent outside it has no picker
/// entry, no details, no turn and no conversation. An agent that is not Ready stays listed with its status; a turn to it
/// fails through kagent's own failure path (D553).
/// </summary>
internal static class OfferedAgents
{
    /// <summary>A supervisor marks an agent whose model reads images; Chat sends images to no other.</summary>
    public const string ImagesLabel = "skanyxx.dev/accepts-images";

    public static async Task<IReadOnlyList<Agent>> ListAsync(KAgentApiClient kagent, CancellationToken ct) =>
        [.. (await kagent.GetAgentsAsync(ct)).Where(ChatOffer.Offers)];

    public static async Task<Agent?> FindAsync(KAgentApiClient kagent, string ns, string name, CancellationToken ct) =>
        (await ListAsync(kagent, ct)).FirstOrDefault(a => a.Namespace == ns && a.Name == name);

    public static bool AcceptsImages(Agent agent) => agent.Labels.TryGetValue(ImagesLabel, out var images) && images == "true";

    /// <summary>kagent's session agent id for <c>namespace/name</c> (its <c>ConvertToPythonIdentifier</c>).</summary>
    public static string SessionAgentId(Agent agent) => $"{agent.Namespace}/{agent.Name}".Replace("-", "_").Replace("/", "__NS__");

    /// <summary>
    /// <c>namespace/name</c> back from a session's agent id, or a tool name kagent gives an agent called as a tool.
    /// Kubernetes names have no <c>_</c>, so the conversion reverses exactly.
    /// </summary>
    public static (string Namespace, string Name)? FromSessionAgentId(string? agentId)
    {
        var at = agentId?.IndexOf("__NS__", StringComparison.Ordinal) ?? -1;
        return at > 0 ? (agentId![..at].Replace('_', '-'), agentId[(at + 6)..].Replace('_', '-')) : null;
    }
}
