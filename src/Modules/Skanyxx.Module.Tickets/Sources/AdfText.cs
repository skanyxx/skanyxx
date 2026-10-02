using System.Text;
using System.Text.Json;

namespace Skanyxx.Module.Tickets.Sources;

/// <summary>Flattens Atlassian Document Format (Jira Cloud's rich text) to plain text; a plain string passes through.</summary>
public static class AdfText
{
    private static readonly HashSet<string> BlockNodes = ["paragraph", "heading", "listItem", "codeBlock", "blockquote", "rule"];

    public static string From(JsonElement node)
    {
        var text = new StringBuilder();
        Append(node, text);
        return text.ToString().Trim();
    }

    private static void Append(JsonElement node, StringBuilder text)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                text.Append(node.GetString());
                return;
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                    Append(child, text);
                return;
            case JsonValueKind.Object:
                var type = node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (type == "text" && node.TryGetProperty("text", out var value))
                    text.Append(value.GetString());
                else if (type == "hardBreak")
                    text.Append('\n');
                if (node.TryGetProperty("content", out var content))
                    Append(content, text);
                if (type is not null && BlockNodes.Contains(type))
                    text.Append('\n');
                return;
            default:
                return;
        }
    }
}
