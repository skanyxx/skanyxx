using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// kagent's own words about a failed turn (a task's status message), made fit for a log line and for builders and up
/// (D553): credentials blanked (<see cref="SecretScan"/>, then <see cref="SecretText"/>), every control, format and
/// line-separator character turned into a space (no forged log line, no bidi trick), and at most
/// <see cref="MaxLength"/> characters, saying so when cut. Beyond the shared redactors it blanks what kagent's Go errors
/// quote from request URLs and headers: the value after any name ending in key, auth, signature, sig, token, secret,
/// password or credential, a user name in a URL, and every bearer or basic value however short.
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
        var redacted = SecretText.Redact(SecretScan.Redact(text, out var cut));
        // The scheme first: "X-Auth-Token: Bearer abc" would otherwise blank only the word Bearer.
        redacted = Scheme.Replace(redacted, m => m.Groups["value"].Value == SecretScan.Redacted ? m.Value : $"{m.Groups["scheme"].Value} {SecretScan.Redacted}");
        redacted = NamedValue.Replace(redacted, m => m.Groups["value"].Value == SecretScan.Redacted ? m.Value : $"{m.Groups["name"].Value}{SecretScan.Redacted}");
        redacted = UrlUser.Replace(redacted, $"://{SecretScan.Redacted}@");
        foreach (var c in redacted)
            clean.Append(char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? ' ' : c);
        var reason = clean.ToString().Trim();
        // SecretScan reads at most MaxRedacted characters: a longer text is cut there already.
        if (reason.Length <= MaxLength && !cut)
            return reason;
        var end = Math.Min(MaxLength, reason.Length);
        if (end > 0 && char.IsHighSurrogate(reason[end - 1]))
            end--;
        return $"{reason[..end]}… (cut: kagent's message has {text.Length.ToString("N0", CultureInfo.InvariantCulture)} characters)";
    }

    private const RegexOptions Linear = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    private static readonly Regex NamedValue = new(
        @"(?<name>[A-Za-z0-9_-]*(?i:key|auth|signature|sig|token|secret|password|passwd|pwd|credential|credentials)[""']?\s{0,4}[:=]\s{0,4}[""']?)(?<value>\[redacted\]|[^\s&""',;)\]}]+)",
        Linear);

    private static readonly Regex UrlUser = new(@"://[^/?#\s@:]+@", Linear);

    private static readonly Regex Scheme = new(@"(?<scheme>\b(?i:bearer|basic))\s+(?<value>\[redacted\]|[^\s""',;]+)", Linear);
}
