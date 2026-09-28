namespace Skanyxx.Module.Memory.Domain;

/// <summary>D080: the one active credential an agent presents on <c>/mcp/memory</c>. Only its SHA-256 is stored.</summary>
public sealed class AgentSecret
{
    public required string AgentId { get; set; }
    public required byte[] SecretHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedBy { get; set; }

    /// <summary>D084: whether the agent's <c>X-User-Id</c> is honoured. Off unless the owner turns it on.</summary>
    public bool ActsForUsers { get; set; }
}
