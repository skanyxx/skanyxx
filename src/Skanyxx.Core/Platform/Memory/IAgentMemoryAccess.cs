namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// What an agent may search and write in memory, for Chat's agent details. Implemented by the memory module; without it
/// the answer is null (no memory installed).
/// </summary>
public interface IAgentMemoryAccess
{
    /// <summary>The agent's grants; empty means memory's default (search company and the person's own, write the person's own, D040).</summary>
    Task<IReadOnlyList<AgentMemoryGrant>?> ForAgentAsync(string agentId, CancellationToken ct);
}
