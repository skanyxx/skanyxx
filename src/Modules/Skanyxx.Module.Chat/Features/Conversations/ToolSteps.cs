using System.Text.Json.Nodes;
using Skanyxx.Module.Chat.Features.Agents;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>Tool calls and results as <see cref="ConversationItem"/>s, redacted (live stream and read-back alike).</summary>
internal static class ToolSteps
{
    public static ConversationItem Call(string? id, string? name, JsonNode? arguments, DateTimeOffset? at) =>
        new(ConversationItemKinds.ToolCall, CallId: id, Tool: name, Agent: AgentOf(name), Arguments: ToolPayload.Redact(arguments), At: at);

    public static ConversationItem Result(string? id, string? name, JsonNode? response, DateTimeOffset? at) =>
        new(ConversationItemKinds.ToolResult, CallId: id, Tool: name, Agent: AgentOf(name),
            Result: ToolPayload.RedactText(ResultText(response)), IsError: EventFields.Flag(response, "isError"), At: at);

    // MCP results are {content: [{type: "text", text}], isError}; an agent called as a tool answers {result: "text"}.
    private static string ResultText(JsonNode? response)
    {
        if (EventFields.Get(response, "content") is JsonArray content && content.All(c => EventFields.Text(c, "text") is not null))
            return string.Join("\n", content.Select(c => EventFields.Text(c, "text")));
        if (EventFields.Text(response, "result") is { } result)
            return result;
        return response?.ToJsonString() ?? "";
    }

    private static string? AgentOf(string? toolName) =>
        MergedAgents.FromSessionAgentId(toolName) is { } agent ? $"{agent.Namespace}/{agent.Name}" : null;
}
