namespace Skanyxx.Module.Tickets.Domain;

/// <summary>A ticket as read from the source. Snapshotted into a run so later edits in Jira do not rewrite it.</summary>
public sealed record Ticket(
    string Key,
    string Title,
    string Description,
    string Type,
    string Status,
    string Priority,
    IReadOnlyList<string> Labels,
    string? Assignee,
    string Url,
    string? UpdatedAt);
