using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Chat.Features;

/// <summary>Without the memory module an agent has no memory to show.</summary>
internal sealed class NoAgentMemoryAccess : IAgentMemoryAccess
{
    public Task<IReadOnlyList<AgentMemoryGrant>?> ForAgentAsync(string agentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentMemoryGrant>?>(null);
}
