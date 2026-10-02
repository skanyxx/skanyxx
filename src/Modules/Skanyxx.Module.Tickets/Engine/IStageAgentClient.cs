using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>One turn of one kagent agent. Throws <see cref="StageAgentException"/> when no usable answer came back.</summary>
public interface IStageAgentClient
{
    Task<string> AskAsync(StageAgent agent, string prompt, CancellationToken ct);
}
