using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Agents;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>Every agent a tree calls must be one people may use now (merged, not suspended): checked on save and on start.</summary>
internal sealed class AgentCheck(IInvestigationAgents agents)
{
    /// <returns>Null when every agent may be used; otherwise the refusal to answer with.</returns>
    public async Task<Outcome<T>?> RefuseAsync<T>(IReadOnlyList<string> used, CancellationToken ct)
    {
        if (used.Count == 0)
            return null;
        IReadOnlyList<AgentChoice> allowed;
        try
        {
            allowed = await agents.ListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return Outcome<T>.Unavailable("kagent did not answer, so the agents could not be checked. Try again in a moment.");
        }

        var missing = used.Except(allowed.Select(a => a.Ref)).ToList();
        return missing.Count == 0
            ? null
            : Outcome<T>.Conflict(default, $"Not an agent you can use now: {string.Join(", ", missing)}. Pick another agent for those steps.", "agent-unavailable");
    }
}
