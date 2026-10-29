using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>
/// Checked on save and on start: every agent a tree calls must be one people may use now (merged, not suspended), and a
/// step reading an earlier answer may call a write-capable agent only when the workflow allows chained writes (D372).
/// On start too, because an agent may have gained a tool or a grant since the workflow was saved.
/// </summary>
internal sealed class AgentCheck(IInvestigationAgents agents, ChainedWrites writes)
{
    /// <returns>Null when the tree may run; otherwise the refusal to answer with.</returns>
    public async Task<Outcome<T>?> RefuseAsync<T>(IReadOnlyList<WorkflowStep> steps, bool allowChainedWrites, CancellationToken ct)
    {
        var used = WorkflowTree.Agents(steps);
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
        if (missing.Count > 0)
            return Outcome<T>.Conflict(default, $"Not an agent you can use now: {string.Join(", ", missing)}. Pick another agent for those steps.", "agent-unavailable");
        if (allowChainedWrites)
            return null;

        var writing = await writes.FindAsync(steps, allowed, ct);
        return writing.Count == 0
            ? null
            : Outcome<T>.Conflict(default,
                $"{string.Join(", ", writing)} can write (tools or memory), and a step calling it reads an earlier agent's answer, which could carry "
                + "instructions planted in it. Point those steps at a read-only agent, or ask the owner or a supervisor to allow chained writes on this workflow.",
                "chained-writes");
    }
}
