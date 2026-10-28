using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// A conversation read back from kagent's session events (D274, D281): the person's messages without Skanyxx's blocks, each
/// tool call and result (redacted), what the model said on its way to them, and each turn's answer with its tokens.
/// kagent lists events newest first; partial (streamed) events are skipped, since their whole event follows.
/// </summary>
internal static class SessionEvents
{
    public static (IReadOnlyList<ConversationItem> Items, ChatUsage Usage) Read(IReadOnlyList<JsonElement> events)
    {
        var items = new List<ConversationItem>();
        var total = ChatUsage.None;
        var turn = ChatUsage.None;
        var ordered = events
            .Select((e, index) => (Data: Data(e), Index: index))
            .Where(e => e.Data is not null && !EventFields.Flag(e.Data, "partial"))
            .Select(e => (e.Data, e.Index, At: EventFields.Time(EventFields.Get(e.Data, "timestamp"))))
            .OrderBy(e => e.At ?? DateTimeOffset.MinValue).ThenByDescending(e => e.Index);
        foreach (var (data, _, at) in ordered)
        {
            var content = EventFields.Get(data, "content") ?? data;
            var role = EventFields.Text(content, "role") ?? EventFields.Text(data, "author");
            var calls = false;
            var text = new StringBuilder();
            foreach (var part in EventFields.Get(content, "parts") as JsonArray ?? [])
                if (EventFields.Get(part, "functionCall") is { } call)
                {
                    // What the model said before calling the tool is a step (D281), shown before the call.
                    if (text.ToString().Trim().Length > 0)
                        items.Add(new ConversationItem(ConversationItemKinds.Step, text.ToString(), At: at));
                    text.Clear();
                    calls = true;
                    items.Add(ToolSteps.Call(EventFields.Text(call, "id"), EventFields.Text(call, "name"), EventFields.Get(call, "args"), at));
                }
                else if (EventFields.Get(part, "functionResponse") is { } response)
                    items.Add(ToolSteps.Result(EventFields.Text(response, "id"), EventFields.Text(response, "name"), EventFields.Get(response, "response"), at));
                else if (EventFields.Text(part, "text") is { } t && !IsThought(part))
                    text.Append(t);

            var usage = Usage(EventFields.Get(data, "usageMetadata"));
            turn = turn.Add(usage);
            total = total.Add(usage);
            if (text.ToString().Trim().Length == 0)
                continue;
            if (role == "user")
            {
                turn = CloseTurn(items, turn);
                var (message, files) = PromptBlocks.Split(text.ToString());
                items.Add(new ConversationItem(ConversationItemKinds.User, message, At: at, Attachments: files));
            }
            else
                items.Add(new ConversationItem(calls ? ConversationItemKinds.Step : ConversationItemKinds.Assistant, text.ToString(), At: at));
        }
        CloseTurn(items, turn);
        return (items, total);
    }

    public static ChatUsage Usage(JsonNode? usage) =>
        usage is null
            ? ChatUsage.None
            : new ChatUsage(EventFields.Number(usage, "promptTokenCount"), EventFields.Number(usage, "candidatesTokenCount"), EventFields.Number(usage, "totalTokenCount"));

    // kagent's Go runtime marks reasoning parts adk_thought, its Python runtime kagent_thought; ADK itself "thought".
    public static bool IsThought(JsonNode? part) =>
        EventFields.Flag(part, "thought") || EventFields.Get(part, "metadata") is { } m && (EventFields.Flag(m, "adkThought") || EventFields.Flag(m, "kagentThought"));

    // The turn's tokens go on its last answer.
    private static ChatUsage CloseTurn(List<ConversationItem> items, ChatUsage turn)
    {
        var last = items.FindLastIndex(i => i.Kind is ConversationItemKinds.Assistant or ConversationItemKinds.User);
        if (last >= 0 && items[last].Kind == ConversationItemKinds.Assistant && turn.Total > 0)
            items[last] = items[last] with { Usage = turn };
        return ChatUsage.None;
    }

    private static JsonNode? Data(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText());
        var data = EventFields.Get(node, "data") ?? node;
        if (data is not JsonValue v || !v.TryGetValue<string>(out var raw))
            return data;
        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
