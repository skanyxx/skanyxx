namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Cuts text to at most <c>length</c> UTF-16 units without splitting a surrogate pair: half an emoji is not
/// valid UTF-8, and Postgres refuses to store it, which would fail the run after its paid model turn.
/// </summary>
public static class TextCut
{
    public static string Take(string text, int length) =>
        text.Length <= length ? text : text[..(char.IsHighSurrogate(text[length - 1]) ? length - 1 : length)];
}
