using System.Text.RegularExpressions;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>The names memory accepts for plugin servers and their headers (the studio's own rules are stricter).</summary>
internal static partial class PluginServerRules
{
    public const int MaxValueChars = 4_096;

    public const int MaxHeaders = 10;

    public static bool IsServer(string? name) => name is { Length: <= 50 } && Server().IsMatch(name);

    public static bool IsPlugin(string? name) => name is { Length: <= 50 } && Plugin().IsMatch(name);

    public static bool IsHeader(string? name) => name is not null && Header().IsMatch(name);

    public static bool IsValue(string? value) => value is { Length: > 0 and <= MaxValueChars } && !value.Any(char.IsControl);

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?\\z")]
    private static partial Regex Server();

    [GeneratedRegex("^[a-z]([-a-z0-9]*[a-z0-9])?\\z")]
    private static partial Regex Plugin();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,63}\\z")]
    private static partial Regex Header();
}
