namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Narrows the library list (requirements.md §3.7). Every field only narrows the scopes the person may already read,
/// so no filter can widen access. Null fields do not filter.
/// </summary>
public sealed record LibraryFilter(
    string? Ticket = null, string? Keyword = null, string? Type = null, string? Origin = null, string? Agent = null, int Offset = 0)
{
    public static readonly LibraryFilter None = new();

    public const int MaxOffset = 10_000;
}
