namespace Skanyxx.Module.Memory.Domain;

/// <summary>D033: search and upsert are separate per scope. Scope <c>personal</c> means the calling user's own.</summary>
public sealed class AgentGrant
{
    public const string CallerPersonal = "personal";

    public required string AgentId { get; set; }
    public required string Scope { get; set; }
    public bool CanSearch { get; set; }
    public bool CanUpsert { get; set; }
}
