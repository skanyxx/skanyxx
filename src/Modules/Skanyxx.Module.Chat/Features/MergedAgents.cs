using Skanyxx.Core.Models;
using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features;

/// <summary>
/// The agents people may talk to: Agent CRs labelled <c>skanyxx.dev/merged: "true"</c> (D4). Today only the seed's YAML
/// carries it; the studio reconciler sets it on merge later (D045, D031). Everything else in kagent (ticket stages,
/// experiments) is not a chat partner. An agent is <c>namespace/name</c>: two merged agents may share a name.
/// </summary>
internal static class MergedAgents
{
    public const string Label = "skanyxx.dev/merged";

    public static async Task<IReadOnlyList<Agent>> ListAsync(KAgentApiClient kagent, CancellationToken ct) =>
        [.. (await kagent.GetAgentsAsync(ct)).Where(a => a.Labels.TryGetValue(Label, out var merged) && merged == "true")];

    /// <summary>kagent's session agent id for <c>namespace/name</c> (its <c>ConvertToPythonIdentifier</c>).</summary>
    public static string SessionAgentId(Agent agent) => $"{agent.Namespace}/{agent.Name}".Replace("-", "_").Replace("/", "__NS__");
}
