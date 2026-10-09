namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// The studio form (D029): who the agent is, its model, its job, skills, MCP tools and memory grants, and optional kagent
/// TTL memory. The memory MCP server is not picked here: an agent gets one exactly when it has a grant, with
/// <c>memory_search</c> for a search grant and <c>memory_upsert</c> for an upsert grant.
/// </summary>
public sealed record AgentDraft(
    string Name,
    string Description,
    string ModelConfig,
    string Instructions,
    IReadOnlyList<string> Skills,
    IReadOnlyList<McpToolChoice> McpTools,
    IReadOnlyList<StudioGrant> Grants,
    int? MemoryTtlDays)
{
    /// <summary>
    /// As typed into a form or an API body, tidied the same way everywhere: trimmed names, Unix line ends, blank list
    /// entries dropped, null lists empty. Validation then judges what is left.
    /// </summary>
    public AgentDraft Normalized() => new(
        Name?.Trim() ?? "",
        Description?.Trim() ?? "",
        ModelConfig?.Trim() ?? "",
        (Instructions ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim(),
        [.. (Skills ?? []).Select(s => s?.Trim() ?? "").Where(s => s.Length > 0)],
        [.. (McpTools ?? []).Where(t => t is not null)
            .Select(t => new McpToolChoice(t.Server?.Trim() ?? "", [.. (t.Tools ?? []).Select(n => n?.Trim() ?? "").Where(n => n.Length > 0)]))],
        [.. (Grants ?? []).Where(g => g is not null).Select(g => g with { Scope = g.Scope?.Trim() ?? "" })],
        MemoryTtlDays is null or 0 ? null : MemoryTtlDays);
}
