namespace Skanyxx.Core.Platform.Memory;

/// <param name="Scopes">What the scope filter offers: the scopes this person may read that can hold cards for them.</param>
/// <param name="Cards">One page, at most <see cref="Limit"/>, from <paramref name="Offset"/>, in the filter's order.</param>
/// <param name="Total">How many cards match every filter (D355), so the page can say "51–100 of 4,812".</param>
/// <param name="TypeCounts">Per card type, how many match every filter except the type: the type chips' counts.</param>
public sealed record LibraryShelf(
    IReadOnlyList<string> Scopes, IReadOnlyList<LibraryEntry> Cards, int Total = 0, IReadOnlyDictionary<string, int>? TypeCounts = null,
    int Offset = 0)
{
    public const int Limit = 50;

    public bool HasMore => Offset + Cards.Count < Total;
}
