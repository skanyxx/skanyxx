using System.Collections.Concurrent;
using Skanyxx.Module.Investigate.Agents;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>
/// The kagent side for tests: the agent list, and each agent's answer as a function of the prompt. By default an agent
/// answers "&lt;agent&gt; says: &lt;prompt&gt;". Every call is recorded in order.
/// </summary>
public sealed class ScriptedAgents : IInvestigationAgents, IInvestigationAgentClient
{
    public const string Researcher = "kagent/researcher";
    public const string Analyst = "kagent/analyst";
    public const string Writer = "team-a/writer";

    public List<AgentChoice> Agents { get; } =
    [
        new(Researcher, "kagent", "researcher", "Looks things up"),
        new(Analyst, "kagent", "analyst", "Reads numbers"),
        new(Writer, "team-a", "writer", "Writes the summary"),
    ];

    public bool Down { get; set; }

    public ConcurrentQueue<AgentCall> Calls { get; } = new();

    public ConcurrentDictionary<string, Func<string, CancellationToken, Task<string>>> Answers { get; } = new();

    public Task<IReadOnlyList<AgentChoice>> ListAsync(CancellationToken ct) =>
        Down ? throw new HttpRequestException("kagent down") : Task.FromResult<IReadOnlyList<AgentChoice>>([.. Agents]);

    public Task<string> AskAsync(string agentRef, string prompt, CancellationToken ct)
    {
        Calls.Enqueue(new AgentCall(agentRef, prompt, DateTime.UtcNow));
        return Answers.TryGetValue(agentRef, out var answer) ? answer(prompt, ct) : Task.FromResult($"{agentRef} says: {prompt}");
    }
}
