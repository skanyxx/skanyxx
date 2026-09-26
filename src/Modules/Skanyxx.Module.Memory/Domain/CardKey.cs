using System.Text.RegularExpressions;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>Machine-usable slug; identity together with the scope (D035, D038).</summary>
public static partial class CardKey
{
    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    // \z, not $: in .NET, $ also matches before a trailing "\n".
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,79}\z")]
    private static partial Regex Pattern();
}
