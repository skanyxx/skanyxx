using System.Text.RegularExpressions;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Who proposed a pull request, written by Skanyxx into its body. Trusted because Skanyxx's account is the only one
/// that can open one (registration off, research/gitea-api.md).
/// </summary>
internal static partial class ProposalTrailer
{
    public static string Write(string agent, string userId, string description) =>
        $"Agent: {agent}\nProposed-by: {userId}\n\n{description}\n\nProposed in Skanyxx studio. A supervisor merges in Skanyxx; the reconciler then applies it to kagent.";

    /// <summary>
    /// A plugin proposal's body (P8): <c>Plugin: &lt;name&gt;</c> first, then the description and install report, one line
    /// per note. That text is the plugin's, so it sits in a code block longer than any backtick run in it: git renders it
    /// as text, never as a link or an image fetched from the reviewer's browser.
    /// </summary>
    public static string WritePlugin(string plugin, string userId, string description, IEnumerable<string> report)
    {
        var text = string.Join("\n", report.Prepend("").Prepend(description));
        var fence = new string('`', Math.Max(3, Backticks().Matches(text).Select(m => m.Length).DefaultIfEmpty(0).Max() + 1));
        return $"Plugin: {plugin}\nProposed-by: {userId}\n\n{fence}text\n{text}\n{fence}\n\n"
            + "Proposed in Skanyxx studio from a plugin. A supervisor merges in Skanyxx; the reconciler then applies its servers and agents to kagent.";
    }

    /// <summary>The plugin a proposal is for (its first line), or null for an agent proposal.</summary>
    public static string? PluginOf(string body) => FirstLine(body, "Plugin: ");

    /// <summary>The agent an agent proposal is for (its first line), or null.</summary>
    public static string? AgentOf(string body) => FirstLine(body, "Agent: ");

    private static string? FirstLine(string body, string prefix) =>
        body.Split('\n')[0] is var first && first.StartsWith(prefix, StringComparison.Ordinal) ? first[prefix.Length..].Trim() : null;

    public static string? ProposedBy(string body) => Line().Match(body) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"^Proposed-by: ([a-z0-9][a-z0-9._@-]{0,127})$", RegexOptions.Multiline)]
    private static partial Regex Line();

    [GeneratedRegex("`+")]
    private static partial Regex Backticks();
}
