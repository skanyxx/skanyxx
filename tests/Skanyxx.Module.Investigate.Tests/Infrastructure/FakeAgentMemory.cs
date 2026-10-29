using System.Collections.Concurrent;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>Memory's agent grants for tests: none (memory's default) unless a test sets some.</summary>
public sealed class FakeAgentMemory : IAgentMemoryAccess
{
    public ConcurrentDictionary<string, IReadOnlyList<AgentMemoryGrant>> Grants { get; } = new();

    public Task<IReadOnlyList<AgentMemoryGrant>?> ForAgentAsync(string agentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentMemoryGrant>?>(Grants.GetValueOrDefault(agentId) ?? []);
}
