using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Investigate.Agents;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features;

/// <summary>
/// D372: a step whose prompt reads an earlier answer (<c>{{steps.x}}</c>) carries text another agent produced, which may
/// carry instructions planted in it. Such a step may call a write-capable agent only on a workflow that allows chained
/// writes. Write-capable, kept simple and failing closed (D386): the agent is not declarative (its tools are not in its
/// spec, so they are unknown), its tool servers are unknown, it calls other agents as tools, it names an MCP tool server
/// that is not on <see cref="InvestigateOptions.ReadOnlyToolServers"/>, or it names a memory server
/// <c>skanyxx-memory-&lt;x&gt;</c> whose agent <c>x</c> holds an explicit grant with upsert: that server writes with
/// <c>x</c>'s grants, whoever calls it. Memory's default grants write only the caller's personal scope, which for a run
/// is the investigation service identity's, read by nobody.
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

    private async Task<bool> CanWriteAsync(AgentChoice agent, CancellationToken ct)
    {
        if (!agent.Declarative || agent.ToolServers is null || agent.CallsAgents)
            return true;
        if (agent.ToolServers.Any(s => !s.StartsWith(MemoryServerPrefix, StringComparison.Ordinal) && !options.Value.ReadOnlyToolServers.Contains(s)))
            return true;
        foreach (var server in agent.ToolServers.Where(s => s.StartsWith(MemoryServerPrefix, StringComparison.Ordinal)))
            if ((await memory.ForAgentAsync(server[MemoryServerPrefix.Length..], ct))?.Any(g => g.Upsert) == true)
                return true;
        return false;
    }
}
