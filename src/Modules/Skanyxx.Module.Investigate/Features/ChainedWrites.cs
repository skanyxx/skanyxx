using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>
/// D372: a step whose prompt reads an earlier answer (<c>{{steps.x}}</c>) carries text another agent produced, which may
/// carry instructions planted in it. Such a step may call a write-capable agent only on a workflow that allows chained
/// writes. Write-capable, kept simple: the agent calls other agents as tools, names an MCP tool server that is not on
/// <see cref="InvestigateOptions.ReadOnlyToolServers"/> (memory's own servers excepted: they are judged by the grants), or
/// holds an explicit memory grant with upsert. Memory's default grants write only the caller's personal scope, which for
/// a run is the investigation service identity's, read by nobody.
/// </summary>
internal sealed class ChainedWrites(IAgentMemoryAccess memory, IOptions<InvestigateOptions> options)
{
    private const string MemoryServerPrefix = "skanyxx-memory-";

    /// <returns>The write-capable agents that steps reading an earlier answer call, each once, in tree order.</returns>
    public async Task<IReadOnlyList<string>> FindAsync(IReadOnlyList<WorkflowStep> steps, IReadOnlyList<AgentChoice> agents, CancellationToken ct)
    {
        var chained = WorkflowTree.Flatten(steps)
            .Where(n => n.Step is { Kind: StepKind.Agent, Agent: not null, Prompt: not null } && PromptTemplate.References(n.Step.Prompt).Count > 0)
            .Select(n => n.Step.Agent!).Distinct();
        var writing = new List<string>();
        foreach (var reference in chained)
            if (agents.FirstOrDefault(a => a.Ref == reference) is { } agent && await CanWriteAsync(agent, ct))
                writing.Add(reference);
        return writing;
    }

    private async Task<bool> CanWriteAsync(AgentChoice agent, CancellationToken ct) =>
        agent.CallsAgents
        || (agent.ToolServers ?? []).Any(s => !s.StartsWith(MemoryServerPrefix, StringComparison.Ordinal) && !options.Value.ReadOnlyToolServers.Contains(s))
        // An agent's memory id is its kagent name (the studio grants by it).
        || (await memory.ForAgentAsync(agent.Name, ct))?.Any(g => g.Upsert) == true;
}
