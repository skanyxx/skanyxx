using System.Text.RegularExpressions;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// requirements.md §1.4: secrets never reach a response, a prompt or a log. A card is shown to people and agents, so
/// what memory extracts on its own is redacted before it is stored, and what goes into a prompt is redacted again;
/// a write that plainly carries a credential is refused. <see cref="Redact"/> is broad (it also masks the value after
/// "password:" or "token="), <see cref="HasCredential"/> only matches token shapes no prose has, so it can refuse.
/// </summary>
public static partial class SecretText
{
    public const string Mask = "[redacted]";

    /// <summary>The text with every credential-looking value replaced by <see cref="Mask"/>.</summary>
    public static string Redact(string text)
    {
        var redacted = PrivateKey().Replace(text, Mask);
        redacted = Token().Replace(redacted, Mask);
        redacted = Bearer().Replace(redacted, m => $"{m.Groups[1].Value} {Mask}");
        redacted = UrlCredentials().Replace(redacted, m => $"{m.Groups[1].Value}{Mask}@");
        redacted = Assignment().Replace(redacted, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Mask}");
        return LongOpaque().Replace(redacted, m => m.Value.Any(char.IsDigit) && m.Value.Any(char.IsLetter) ? Mask : m.Value);
    }

    /// <summary>A private key, a provider token (sk-, ghp_, AKIA…, xox…, a JWT, a Skanyxx memory secret) or URL credentials.</summary>
    public static bool HasCredential(string? text) =>
        text is not null && (PrivateKey().IsMatch(text) || Token().IsMatch(text) || UrlCredentials().IsMatch(text));

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?(-----END [A-Z ]*PRIVATE KEY-----|\z)", RegexOptions.Singleline)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(" +
        @"(sk|pk|rk)-[A-Za-z0-9_-]{16,}" +
        @"|skx_[a-z]+_[A-Za-z0-9_-]{16,}" +
        @"|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}" +
        @"|xox[abprs]-[A-Za-z0-9-]{10,}" +
        @"|(AKIA|ASIA)[0-9A-Z]{16}" +
        @"|AIza[0-9A-Za-z_-]{30,}" +
        @"|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{5,}" +
        @")")]
    private static partial Regex Token();

    [GeneratedRegex(@"\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{12,}", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"([a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(
        @"\b(password|passwd|pwd|passphrase|secret|client[_-]?secret|api[_-]?key|access[_-]?key|auth[_-]?token|token|private[_-]?key)\b" +
        @"(\s*[:=]\s*|\s+(?:is|was)\s+)(""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Assignment();

    // A long run with letters and digits mixed is an opaque value (a key, a hash) rather than a word.
    [GeneratedRegex(@"(?<![A-Za-z0-9+/_-])[A-Za-z0-9+/_-]{40,}={0,2}")]
    private static partial Regex LongOpaque();
}
