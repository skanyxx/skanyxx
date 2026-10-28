using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Analytics;

namespace Skanyxx.Core.Services;

/// <summary>
/// The tokens a blocking A2A answer reports. kagent's runtimes put the model's <c>usage_metadata</c> into event metadata
/// (Python <c>kagent_usage_metadata</c>, Go <c>adk_usage_metadata</c>), once per model call. Every message that carries one
/// counts once (a message seen in both history and status by its id); artifacts, then the task's own metadata (only the
/// last call's copy) are the fallbacks when no message does. Any other shape is no usage: this never fails a chat. No usage anywhere is <see cref="TokenUsage.None"/>.
/// </summary>
public static class A2AUsage
{
    private static readonly string[] Keys = ["kagent_usage_metadata", "adk_usage_metadata"];

    public static TokenUsage From(JsonNode? result)
    {
        if (result is not JsonObject)
            return TokenUsage.None;

        var seen = new HashSet<string>();
        var found = new List<JsonObject>();
        foreach (var message in Messages(result))
            if (Usage(message["metadata"]) is { } usage && (Text(message["messageId"]) is not { } id || seen.Add(id)))
                found.Add(usage);
        if (found.Count == 0)
            foreach (var artifact in (result["artifacts"] as JsonArray ?? []).OfType<JsonObject>())
                if (Usage(artifact["metadata"]) is { } usage)
                    found.Add(usage);
        if (found.Count == 0 && Usage(result["metadata"]) is { } last)
            found.Add(last);
        if (found.Count == 0)
            return TokenUsage.None;

        long input = 0, output = 0;
        foreach (var usage in found)
        {
            var prompt = Count(usage, "promptTokenCount", "prompt_token_count") + Count(usage, "toolUsePromptTokenCount", "tool_use_prompt_token_count");
            var candidates = Count(usage, "candidatesTokenCount", "candidates_token_count") + Count(usage, "thoughtsTokenCount", "thoughts_token_count");
            if (candidates == 0)
                candidates = Math.Max(0, Count(usage, "totalTokenCount", "total_token_count") - prompt);
            input += prompt;
            output += candidates;
        }
        return new TokenUsage(input, output, true);
    }

    private static IEnumerable<JsonObject> Messages(JsonNode result)
    {
        if (Text(result["kind"]) == "message" && result is JsonObject message)
            yield return message;
        foreach (var entry in result["history"] as JsonArray ?? [])
            if (entry is JsonObject m)
                yield return m;
        if ((result["status"] as JsonObject)?["message"] is JsonObject status)
            yield return status;
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static JsonObject? Usage(JsonNode? metadata) =>
        metadata is JsonObject m ? Keys.Select(k => m[k]).OfType<JsonObject>().FirstOrDefault() : null;

    private static long Count(JsonObject usage, string camel, string snake) =>
        (usage[camel] ?? usage[snake]) is JsonValue v && v.GetValueKind() == JsonValueKind.Number
            && long.TryParse(v.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
