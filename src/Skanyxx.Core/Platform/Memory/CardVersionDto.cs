namespace Skanyxx.Core.Platform.Memory;

/// <summary>A replaced version of a card, newest first in a history: the whole card as it was.</summary>
public sealed record CardVersionDto(
    int Version, string Scope, string Key, string Type, string What, string Why, string Who, string Status,
    IReadOnlyList<string> Tickets, DateTime UpdatedAt,
    IReadOnlyList<string> Links, IReadOnlyList<string> Keywords, string? Body, string? Source, string Origin,
    string? AgentId, string? SupersededBy);
