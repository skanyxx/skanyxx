using System.Text.RegularExpressions;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// What a card may point at: another card (<c>scope/key</c>), a chat or a run (<c>chat:&lt;id&gt;</c>,
/// <c>run:&lt;id&gt;</c>), or an http(s) URL. Pointers only: memory never follows them.
/// </summary>
public static partial class CardLinks
{
    public static bool IsValid(string? link) =>
        link is { Length: > 0 and <= CardLimits.LinkLength }
        && (IsCard(link) || Work().IsMatch(link)
            || (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !link.Any(char.IsWhiteSpace)));

    /// <summary><c>scope/key</c> of a card.</summary>
    public static bool IsCard(string link) =>
        link.LastIndexOf('/') is var slash and > 0
        && Scope.TryParse(link[..slash], out _) && CardKey.IsValid(link[(slash + 1)..]);

    /// <summary>The scope a card link points into; null for any other link.</summary>
    public static Scope? CardScope(string link) => IsCard(link) ? Scope.Parse(link[..link.LastIndexOf('/')]) : null;

    public static bool IsKeyword(string? word) => word is not null && Keyword().IsMatch(word);

    [GeneratedRegex(@"^(chat|run):[A-Za-z0-9_-]{1,128}\z")]
    private static partial Regex Work();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,39}\z")]
    private static partial Regex Keyword();
}
