using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// Tool arguments and results as shown in Chat (requirements.md §1.4: no secret in a response). A string under a
/// credential-looking key is masked whatever it looks like; every other string goes through
/// <see cref="SecretText.Redact"/>. The text is cut at <see cref="MaxChars"/>.
/// </summary>
internal static partial class ToolPayload
{
    public const int MaxChars = 4_000;

    public static string Redact(JsonNode? node)
    {
        var copy = node?.DeepClone();
        if (Masked(copy, secretKey: false) is { } value)
            return Cut(value.ToJsonString());
        MaskWithin(copy, secretKey: false);
        return Cut(copy?.ToJsonString() ?? "");
    }

    /// <summary>A result that is JSON is masked like arguments; plain text is redacted.</summary>
    public static string RedactText(string text)
    {
        try
        {
            if (text.TrimStart() is ['{', ..] or ['[', ..])
                return Redact(JsonNode.Parse(text));
        }
        catch (JsonException)
        {
        }
        return Cut(SecretText.Redact(text));
    }

    // Containers are masked in place; a string is replaced by its masked copy.
    private static void MaskWithin(JsonNode? node, bool secretKey)
    {
        if (node is JsonObject o)
            foreach (var key in o.Select(p => p.Key).ToList())
            {
                var secret = secretKey || SecretKey().IsMatch(key);
                if (Masked(o[key], secret) is { } value)
                    o[key] = value;
                else
                    MaskWithin(o[key], secret);
            }
        else if (node is JsonArray a)
            for (var i = 0; i < a.Count; i++)
                if (Masked(a[i], secretKey) is { } value)
                    a[i] = value;
                else
                    MaskWithin(a[i], secretKey);
    }

    private static JsonValue? Masked(JsonNode? node, bool secretKey) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? JsonValue.Create(secretKey ? SecretText.Mask : SecretText.Redact(s)) : null;

    private static string Cut(string text) => text.Length <= MaxChars ? text : text[..MaxChars] + " … (cut)";

    [GeneratedRegex(@"pass(word|wd|phrase)?|pwd|secret|token|api[_-]?key|access[_-]?key|authori[sz]ation|credential|private[_-]?key|cookie", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();
}
