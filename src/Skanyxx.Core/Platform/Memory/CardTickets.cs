using System.Text.RegularExpressions;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>Ticket keys on cards (<c>SDB-58</c>): the shape the tickets module accepts, uppercase.</summary>
public static partial class CardTickets
{
    public static bool IsValid(string? key) => key is not null && Exact().IsMatch(key);

    /// <summary>Keys mentioned in <paramref name="text"/>, any case, uppercased, first seen first.</summary>
    public static IReadOnlyList<string> FindIn(string? text, int max) => text is null
        ? []
        : Mention().Matches(text).Select(m => m.Value.ToUpperInvariant()).Distinct().Take(max).ToList();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{0,29}-[1-9][0-9]{0,9}\z")]
    private static partial Regex Exact();

    // Word-bounded so "abc-def-12" does not yield "DEF-12" out of a slug, and "v2-1" style noise needs a letter first.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_-])[A-Za-z][A-Za-z0-9_]{0,29}-[1-9][0-9]{0,9}(?![\p{L}\p{N}_-])")]
    private static partial Regex Mention();
}
