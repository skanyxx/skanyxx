using System.Text.RegularExpressions;

namespace Skanyxx.Core.Platform;

/// <summary>The id shape used for users and agents across modules (lowercase, no whitespace or newline).</summary>
public static partial class Identifier
{
    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id);

    // \z, not $: in .NET, $ also matches before a trailing "\n".
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._@-]{0,127}\z")]
    private static partial Regex Pattern();
}
