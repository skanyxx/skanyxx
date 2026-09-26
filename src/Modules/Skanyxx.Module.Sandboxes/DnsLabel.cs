using System.Text.RegularExpressions;

namespace Skanyxx.Module.Sandboxes;

/// <summary>AX names (tasks, workspaces, atespaces) must be RFC 1123 labels: lowercase alphanumerics and '-', at most 63.</summary>
internal static partial class DnsLabel
{
    // \z, not $: in .NET, $ also matches before a trailing "\n".
    public const string Pattern = @"^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?\z";

    public static bool IsValid(string? value) => value is not null && Regex().IsMatch(value);

    [GeneratedRegex(Pattern)]
    private static partial Regex Regex();
}
