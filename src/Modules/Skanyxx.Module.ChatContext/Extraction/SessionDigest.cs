namespace Skanyxx.Module.ChatContext.Extraction;

/// <summary>Everything the index derives from a conversation.</summary>
public sealed record SessionDigest(
    IReadOnlyDictionary<string, int> KeywordWeights, IReadOnlyList<string> Categories, IReadOnlyList<string> TicketKeys, string Outcome)
{
    public const int MaxKeywords = 12;
    public const int MaxWeights = 64;
    public const int MaxCategories = 5;

    public static readonly SessionDigest Empty = new(new Dictionary<string, int>(), [], [], "");

    /// <summary>Heaviest first; ties alphabetical so the order is stable.</summary>
    public IReadOnlyList<string> Keywords =>
        [.. KeywordWeights.OrderByDescending(w => w.Value).ThenBy(w => w.Key, StringComparer.Ordinal).Take(MaxKeywords).Select(w => w.Key)];
}
