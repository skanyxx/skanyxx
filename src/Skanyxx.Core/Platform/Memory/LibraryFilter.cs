namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Narrows and orders the library list (requirements.md §3.7, D355). Every field only narrows the scopes the person may
/// already read, so no filter can widen access. Null fields do not filter. <paramref name="From"/> and
/// <paramref name="To"/> are inclusive UTC days of the last update; <paramref name="Owner"/> is the card's last writer
/// (<c>who</c>); <paramref name="Sort"/> is one of <see cref="Sorts"/> (null: relevance with text, newest without).
/// </summary>
public sealed record LibraryFilter(
    string? Ticket = null, string? Keyword = null, string? Type = null, string? Origin = null, string? Agent = null, int Offset = 0,
    string? Owner = null, DateOnly? From = null, DateOnly? To = null, string? Sort = null)
{
    public static readonly LibraryFilter None = new();

    public const int MaxOffset = 10_000;

    public static readonly IReadOnlyList<string> Sorts = ["relevance", "newest", "oldest", "what"];
}
