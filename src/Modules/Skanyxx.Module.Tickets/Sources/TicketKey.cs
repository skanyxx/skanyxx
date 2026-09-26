using System.Text.RegularExpressions;

namespace Skanyxx.Module.Tickets.Sources;

/// <summary>A Jira issue key (<c>SDB-12</c>). Checked before it reaches a URL path.</summary>
public static partial class TicketKey
{
    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{0,29}-[1-9][0-9]{0,9}\z")]
    private static partial Regex Pattern();
}
