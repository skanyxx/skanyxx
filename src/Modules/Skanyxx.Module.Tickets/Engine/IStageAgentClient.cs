using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>One turn of one kagent agent. Throws <see cref="StageAgentException"/> when no usable answer came back.</summary>
public interface IStageAgentClient
{
    Task<StageAnswer> AskAsync(StageAgent agent, string prompt, CancellationToken ct);

    /// <summary>
    /// The agent's ModelConfig as the token quota's price key (<c>namespace/name</c>, D387), read from its spec;
    /// <c>unknown</c> when kagent does not say (a BYO agent, or kagent not answering). Never throws for that.
    /// </summary>
    Task<string> ModelRefAsync(StageAgent agent, CancellationToken ct);
}
