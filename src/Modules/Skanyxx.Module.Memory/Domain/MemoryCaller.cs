using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// Who is asking. REST: a signed-in human, from the authenticated principal. MCP: the agent that owns the presented
/// secret (D080), plus the user it vouches for in X-User-Id when the secret allows it (D084).
/// </summary>
public sealed record MemoryCaller(string? UserId, string? AgentId, bool IsSupervisor = false, bool IsOwner = false)
{
    /// <summary>A signed-in person (REST, the Host's library page); never an agent.</summary>
    public static MemoryCaller For(LibraryUser user) => new(user.UserId, null, user.IsSupervisor, user.IsOwner);

    public bool IsAgent => AgentId is not null;

    public Scope? PersonalScope => UserId is null ? null : Scope.Personal(UserId);

    public string Who => (UserId, AgentId) switch
    {
        ({ } user, { } agent) => $"{user} via {agent}",
        (null, { } agent) => $"agent:{agent}",
        ({ } user, null) => user,
        _ => throw new InvalidOperationException("A caller without identity reached a write; validation should have rejected it.")
    };

    public string RateLimitKey => AgentId is null ? $"user:{UserId}" : $"agent:{AgentId}";
}
