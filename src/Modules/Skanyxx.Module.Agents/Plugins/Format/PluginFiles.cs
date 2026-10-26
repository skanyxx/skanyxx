using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>Reading a plugin's files: size and JSON shape bounded, component paths kept inside the plugin.</summary>
internal static partial class PluginFiles
{
    public const int MaxFileBytes = 256 * 1024;

    public const int MaxJsonDepth = 16;

    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = MaxJsonDepth, AllowDuplicateProperties = false };

    /// <summary>The file as text (UTF-8, byte order mark dropped), or why it cannot be read.</summary>
    public static (string? Text, string? Problem) ReadText(PluginTree tree, string path)
    {
        if (tree.Read(path) is not { } bytes)
            return (null, $"{path} does not exist.");
        if (bytes.Length > MaxFileBytes)
            return (null, $"{path} is larger than {MaxFileBytes / 1024} KB.");
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return (text.TrimStart('﻿'), null);
        }
        catch (DecoderFallbackException)
        {
            return (null, $"{path} is not UTF-8 text.");
        }
    }

    /// <summary>
    /// The file as a JSON object, or why it is not one (too large, not JSON, nested deeper than <see cref="MaxJsonDepth"/>,
    /// a key twice: Claude Code takes the last one, so the two would read different plugins).
    /// </summary>
    public static (JsonObject? Json, string? Problem) ReadObject(PluginTree tree, string path)
    {
        var (text, problem) = ReadText(tree, path);
        if (text is null)
            return (null, problem);
        try
        {
            return JsonNode.Parse(text, documentOptions: JsonOptions) is JsonObject json
                ? (json, null)
                : (null, $"{path} must be a JSON object.");
        }
        catch (JsonException ex)
        {
            return (null, $"{path} is not valid JSON: {ex.Message.Split('\n')[0]}");
        }
    }

    /// <summary>
    /// A component path from a manifest (<c>./agents/x.md</c>) as a tree path (<c>agents/x.md</c>), or null when it breaks
    /// Claude Code's rule: relative to the plugin root, starting with <c>./</c>, no <c>..</c>.
    /// </summary>
    public static string? ComponentPath(string raw)
    {
        if (!raw.StartsWith("./", StringComparison.Ordinal) || raw.Contains('\\'))
            return null;
        var segments = raw[2..].TrimEnd('/').Split('/');
        return segments.Any(s => s is "" or "." or "..") ? null : string.Join('/', segments);
    }

    /// <summary>A key of an object; null for anything that is not one.</summary>
    public static JsonNode? At(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// One line for a description: whitespace runs (a YAML block scalar's newlines) as one space, invisible formatting
    /// characters dropped, since they make a review page show something other than what is stored.
    /// </summary>
    public static string OneLine(string? text) => Whitespace().Replace(new string([.. (text ?? "").Where(c =>
        char.GetUnicodeCategory(c) != UnicodeCategory.Format && !(char.IsControl(c) && !char.IsWhiteSpace(c)))]), " ").Trim();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
