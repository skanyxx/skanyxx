using System.Globalization;
using System.Text.Json.Nodes;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// kagent's stored events in either runtime's spelling: the Python ADK writes camelCase (some parts snake_case), the Go
/// runtime PascalCase for the event itself (<c>Content</c>, <c>Partial</c>, <c>Author</c>, <c>Timestamp</c>).
/// </summary>
internal static class EventFields
{
    public static JsonNode? Get(JsonNode? node, string camel) =>
        node is JsonObject o
            ? o[camel] ?? o[char.ToUpperInvariant(camel[0]) + camel[1..]] ?? o[Snake(camel)] ?? o[camel.ToUpperInvariant()]
            : null;

    public static string? Text(JsonNode? node, string camel) => Get(node, camel) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool Flag(JsonNode? node, string camel) => Get(node, camel) is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public static long Number(JsonNode? node, string camel) => Get(node, camel) is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;

    /// <summary>Seconds (Python), milliseconds, or an ISO string (Go).</summary>
    public static DateTimeOffset? Time(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<double>(out var n) => n < 1e12
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(n * 1000))
            : DateTimeOffset.FromUnixTimeMilliseconds((long)n),
        JsonValue v when v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) => t,
        _ => null
    };

    private static string Snake(string camel) => string.Concat(camel.Select(c => char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : c.ToString()));
}
