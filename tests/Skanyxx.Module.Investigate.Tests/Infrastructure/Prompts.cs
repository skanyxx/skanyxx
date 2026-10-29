using System.Text.RegularExpressions;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>
/// What a rendered prompt holds for each placeholder: the text in its untrusted-data envelope (D372). Envelopes carry a
/// random nonce per render (D389); <see cref="Plain"/> sets every one to <see cref="Nonce"/> so prompts compare exactly.
/// </summary>
public static partial class Prompts
{
    public const string Nonce = "n";

    public static string Input(string text) => PromptTemplate.Envelope("the investigation's input, written by a person", text, Nonce);

    public static string Step(string id, string text) => PromptTemplate.Envelope($"the answer of step {id}, written by another agent", text, Nonce);

    public static string Plain(string prompt) => Nonces().Replace(prompt, "${marker}" + Nonce);

    [GeneratedRegex("(?<marker>SKANYXX[-_]DATA:)[0-9a-f]{32}")]
    private static partial Regex Nonces();
}
