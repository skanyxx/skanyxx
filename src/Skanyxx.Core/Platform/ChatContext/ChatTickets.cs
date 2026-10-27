using System.Text.RegularExpressions;

namespace Skanyxx.Core.Platform.ChatContext;

/// <summary>A Jira issue key (<c>SDB-58</c>): the same shape the tickets module accepts.</summary>
public static partial class ChatTickets
{
    public const int MaxPerSession = 20;

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{0,29}-[1-9][0-9]{0,9}\z")]
    private static partial Regex Pattern();
}
