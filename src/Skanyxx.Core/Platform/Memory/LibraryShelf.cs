namespace Skanyxx.Core.Platform.Memory;

/// <param name="Scopes">What the scope filter offers: the scopes this person may read that can hold cards for them.</param>
/// <param name="Cards">Best match first (or newest first without text), at most <see cref="Limit"/>.</param>
public sealed record LibraryShelf(IReadOnlyList<string> Scopes, IReadOnlyList<LibraryEntry> Cards)
{
    public const int Limit = 50;
}
