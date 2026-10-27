namespace Skanyxx.Core.Platform.Memory;

/// <summary>A replaced version of a card, newest first in a history.</summary>
public sealed record CardVersionDto(
    int Version, string Scope, string Key, string Type, string What, string Why, string Who, string Status,
    IReadOnlyList<string> Tickets, DateTime UpdatedAt);
