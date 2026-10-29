using System.Text.RegularExpressions;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// A step's prompt: plain text with <c>{{input}}</c> (what the investigation was started with) and
/// <c>{{steps.&lt;id&gt;}}</c> (the output of an earlier step). Anything else in braces is left as written.
/// </summary>
public static partial class PromptTemplate
{
    /// <summary>The step ids a template reads, each once.</summary>
    public static IReadOnlyList<string> References(string template) =>
        [.. Placeholder().Matches(template).Where(m => m.Groups["step"].Success).Select(m => m.Groups["step"].Value).Distinct()];

    /// <summary>The template with its placeholders filled; a step with no output yet reads as empty.</summary>
    public static string Render(string template, string input, IReadOnlyDictionary<string, string> outputs) =>
        Placeholder().Replace(template, m => m.Groups["step"].Success
            ? outputs.GetValueOrDefault(m.Groups["step"].Value, "")
            : input);

    [GeneratedRegex(@"\{\{\s*(?:input|steps\.(?<step>[a-z0-9][a-z0-9-]{0,31}))\s*\}\}")]
    private static partial Regex Placeholder();
}
