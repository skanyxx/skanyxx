namespace Skanyxx.Module.ChatContext.Extraction;

internal static class TextCut
{
    /// <summary>
    /// At most <paramref name="max"/> characters, ending in "…" when cut; never between the two halves of a surrogate pair
    /// (an emoji), which would not encode as UTF-8 for Postgres.
    /// </summary>
    public static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var end = max - 1;
        if (char.IsHighSurrogate(text[end - 1]))
            end--;
        return text[..end] + "…";
    }
}
