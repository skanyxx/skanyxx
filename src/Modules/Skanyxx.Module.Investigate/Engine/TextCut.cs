namespace Skanyxx.Module.Investigate.Engine;

internal static class TextCut
{
    /// <summary>At most <paramref name="max"/> characters, saying so when something was cut.</summary>
    public static string Take(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max), $"\n[cut: {text.Length - max} more characters]");
}
