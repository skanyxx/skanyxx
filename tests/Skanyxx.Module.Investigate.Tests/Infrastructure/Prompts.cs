using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>What a rendered prompt holds for each placeholder: the text in its untrusted-data envelope (D372).</summary>
public static class Prompts
{
    public static string Input(string text) => PromptTemplate.Envelope("the investigation's input, written by a person", text);

    public static string Step(string id, string text) => PromptTemplate.Envelope($"the answer of step {id}, written by another agent", text);
}
