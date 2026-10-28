using System.Text;
using System.Text.RegularExpressions;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Sessions;

/// <summary>
/// Deterministic cuts of chat and run text into card fields: no model call, so extraction costs no tokens and cannot be
/// talked into writing more than a line (memory-store.md: a 10k-token session is 0–few cards, not 10k tokens).
/// </summary>
internal static partial class SessionText
{
    private static readonly HashSet<string> StopWords =
    [
        "about", "after", "again", "redacted", "also", "and", "are", "because", "been", "before", "being", "but", "can", "could", "did",
        "does", "doing", "done", "for", "from", "had", "has", "have", "help", "here", "how", "into", "is", "it", "its",
        "just", "like", "make", "more", "most", "need", "not", "now", "only", "other", "our", "out", "over", "please",
        "should", "some", "than", "that", "the", "their", "them", "then", "there", "these", "they", "this", "those",
        "through", "very", "want", "was", "were", "what", "when", "where", "which", "while", "who", "why", "will", "with",
        "would", "you", "your"
    ];

    /// <summary>The text on one line (whitespace collapsed, control characters dropped), cut at a word to <paramref name="max"/>.</summary>
    public static string Line(string text, int max)
    {
        var line = Whitespace().Replace(new string(text.Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray()), " ").Trim();
        return Cut(line, max);
    }

    /// <summary>The first sentences that fit in <paramref name="max"/>; markdown fences and their code are left out.</summary>
    public static string Gist(string text, int max)
    {
        var prose = Line(Fence().Replace(text, " "), int.MaxValue);
        var cut = new StringBuilder();
        foreach (Match sentence in Sentence().Matches(prose))
        {
            if (cut.Length + sentence.Length + 1 > max)
                break;
            cut.Append(cut.Length > 0 ? " " : "").Append(sentence.Value.Trim());
        }
        return cut.Length > 0 ? cut.ToString() : Cut(prose, max);
    }

    public static int Words(string text) => Word().Count(text);

    /// <summary>Topic words: lowercase, not a stop word, at least 4 letters, most frequent first.</summary>
    public static IEnumerable<string> Keywords(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length is >= 4 and <= CardLimits.KeywordLength && !StopWords.Contains(w) && !w.All(char.IsDigit) && CardLinks.IsKeyword(w))
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key);

    /// <summary>A card key from free text: lowercase, '-' between words, at most 80 characters.</summary>
    public static string Slug(string text) =>
        Cut(NotSlug().Replace(text.ToLowerInvariant(), "-").Trim('-'), CardKey.MaxLength).TrimEnd('-');

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var at = text.LastIndexOf(' ', max - 1);
        return (at > max / 2 ? text[..at] : text[..(max - 1)]).TrimEnd() + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("```.*?(```|$)", RegexOptions.Singleline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"[^.!?]+[.!?]*")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}
