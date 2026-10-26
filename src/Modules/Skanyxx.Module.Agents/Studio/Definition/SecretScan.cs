using System.Text.RegularExpressions;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// What a credential looks like in text a plugin brings (a note, an agent's instructions, a header template): private
/// keys, well-known token formats, a JWT, a bearer token written out. A plugin PR holding one is refused: header values
/// go to the server's Secret, never to git (P3).
/// </summary>
internal static partial class SecretScan
{
    /// <returns>What kind of credential the text seems to hold, or null when none.</returns>
    public static string? Find(string text) =>
        Patterns().Match(text) is { Success: true } m
            ? m.Groups.Values.Skip(1).FirstOrDefault(g => g.Success)?.Name switch
            {
                "key" => "a private key",
                "jwt" => "a JWT",
                "bearer" => "a bearer token",
                _ => "an API token"
            }
            : null;

    [GeneratedRegex(@"(?<key>-----BEGIN [A-Z ]*PRIVATE KEY-----)"
        + @"|(?<token>\bgh[pousr]_[A-Za-z0-9]{36,}|\bgithub_pat_[A-Za-z0-9_]{22,}|\bsk-[A-Za-z0-9_-]{20,}|\bxox[abprs]-[A-Za-z0-9-]{10,}|\bAKIA[0-9A-Z]{16}\b|\bAIza[0-9A-Za-z_-]{35}|\bglpat-[A-Za-z0-9_-]{20,})"
        + @"|(?<jwt>\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.)"
        + @"|(?<bearer>\b[Bb]earer\s+[A-Za-z0-9._~+/=-]{20,})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Patterns();
}
