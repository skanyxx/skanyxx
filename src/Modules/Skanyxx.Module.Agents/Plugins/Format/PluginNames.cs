using System.Text.RegularExpressions;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>How plugin, server and agent names from a plugin become names Skanyxx and kagent accept (P2).</summary>
internal static partial class PluginNames
{
    public const int MaxServerName = 50;

    /// <summary>
    /// A plugin name: Claude Code's marketplace rule (letters, digits, '.', '_', '-', starting with a letter or digit),
    /// also ending with one and at most 63 long, so it can be a Kubernetes label value on what it installs.
    /// </summary>
    public static bool IsValidPluginName(string? name) => name is not null && PluginName().IsMatch(name);

    /// <summary>Lowercase, every run of other characters (spaces, '_', ':', '.') as one '-', no '-' at the ends, at most <paramref name="max"/>.</summary>
    public static string Kebab(string raw, int max)
    {
        var name = NotKebab().Replace(raw.Trim().ToLowerInvariant(), "-").Trim('-');
        return name.Length > max ? name[..max].TrimEnd('-') : name;
    }

    /// <summary><c>&lt;plugin&gt;-&lt;server&gt;</c> as a Kubernetes name of at most <see cref="MaxServerName"/>.</summary>
    public static string ServerName(string plugin, string server) => Kebab($"{plugin}-{server}", MaxServerName);

    /// <summary>A server name the studio owns or a preview uses (P2).</summary>
    public static bool IsReservedServer(string name) =>
        name.StartsWith("skanyxx-", StringComparison.Ordinal) || name.StartsWith("preview-", StringComparison.Ordinal);

    public static string AgentName(string raw) => Kebab(raw, StudioNames.MaxNameLength);

    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9._-]{0,61}[A-Za-z0-9])?\\z")]
    private static partial Regex PluginName();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotKebab();
}
