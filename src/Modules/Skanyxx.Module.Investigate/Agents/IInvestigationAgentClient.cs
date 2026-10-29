namespace Skanyxx.Module.Investigate.Agents;

/// <summary>One turn of one kagent agent. Throws <see cref="AgentCallException"/> when no usable answer came back.</summary>
public interface IInvestigationAgentClient
{
    Task<string> AskAsync(string agentRef, string prompt, CancellationToken ct);
}
