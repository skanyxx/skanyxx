using System.Globalization;
using System.Text;

namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// kagent's own words about a failed turn (a task's status message), made fit for a log line and for builders and up
/// (D553): credentials blanked (<see cref="SecretScan"/>, then <see cref="SecretText"/>), every control, format and
/// line-separator character turned into a space (no forged log line, no bidi trick), and at most
/// <see cref="MaxLength"/> characters, saying so when cut.
/// </summary>
public static class ChatFailureReason
{
    public const int MaxLength = 2_000;

    /// <returns>Null when kagent said nothing.</returns>
    public static string? Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var clean = new StringBuilder(text.Length);
        foreach (var c in SecretText.Redact(SecretScan.Redact(text)))
            clean.Append(char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? ' ' : c);
        var reason = clean.ToString().Trim();
        // SecretScan reads at most MaxRedacted characters: a longer text is cut there already.
        if (reason.Length <= MaxLength && text.Length <= SecretScan.MaxRedacted)
            return reason;
        var end = Math.Min(MaxLength, reason.Length);
        if (end > 0 && char.IsHighSurrogate(reason[end - 1]))
            end--;
        return $"{reason[..end]}… (cut: kagent's message has {text.Length.ToString("N0", CultureInfo.InvariantCulture)} characters)";
    }
}
