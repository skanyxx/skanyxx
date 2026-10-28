using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Audit;

/// <summary>
/// What an audit row may never hold (requirements.md §1.4): a password, token, secret, key, connection string, cookie or
/// authorization header — masked by the property's name — prompt, chat or card text — masked by name too — and any
/// credential-shaped string (<see cref="SecretText.Redact"/>). Long strings are cut, saying so, since an audit row names
/// what changed rather than carrying content. Applied when a row is written and again when one is read.
/// </summary>
internal static class AuditRedactor
{
    public const int MaxString = 256;

    private static readonly string[] SecretSuffixes =
        ["password", "passwd", "passphrase", "secret", "token", "apikey", "privatekey", "accesskey", "connectionstring",
         "cookie", "cookies", "authorization", "credential", "credentials"];

    private static readonly HashSet<string> ContentNames =
        ["prompt", "systemprompt", "instructions", "message", "messages", "content", "body", "chat", "transcript", "reply", "text", "card"];

    public static JsonNode? Redact(JsonNode? node) => node switch
    {
        JsonObject obj => RedactObject(obj),
        JsonArray array => new JsonArray([.. array.Select(Redact)]),
        JsonValue value when value.GetValueKind() == JsonValueKind.String => JsonValue.Create(Text(value.GetValue<string>())),
        _ => node?.DeepClone()
    };

    /// <summary>A stored <c>Details</c> JSON text, redacted; null stays null.</summary>
    public static string? Redact(string? json) => json is null ? null : Redact(JsonNode.Parse(json))?.ToJsonString();

    public static string Text(string text)
    {
        var redacted = SecretText.Redact(text);
        return redacted.Length <= MaxString ? redacted : $"{redacted[..MaxString]}… [cut, {redacted.Length} chars]";
    }

    public static bool IsSensitiveName(string name)
    {
        var normalized = new string([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
        return ContentNames.Contains(normalized) || SecretSuffixes.Any(normalized.EndsWith);
    }

    private static JsonObject RedactObject(JsonObject obj)
    {
        var copy = new JsonObject();
        foreach (var (name, value) in obj)
            copy[name] = IsSensitiveName(name) && value is not null ? JsonValue.Create(SecretText.Mask) : Redact(value);
        return copy;
    }
}
