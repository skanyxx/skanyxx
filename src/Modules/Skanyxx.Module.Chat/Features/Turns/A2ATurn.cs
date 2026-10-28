using System.Text;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// Reads one turn's A2A stream from kagent (task, status updates, artifact updates) into <see cref="ChatStreamEvent"/>s.
/// Partial chunks grow the draft; a whole intermediate message replaces it; a tool call turns the draft into a step;
/// the final status message or the artifact is the answer. Reasoning parts are skipped; tool payloads are redacted.
/// </summary>
internal sealed class A2ATurn
{
    private string _draft = "";
    private string _answer = "";

    public string? TaskId { get; private set; }

    public ChatUsage Usage { get; private set; } = ChatUsage.None;

    /// <summary>Why the turn did not complete, in plain words; null while it is fine.</summary>
    public string? Failure { get; private set; }

    /// <summary>The answer, else the last thing the agent said.</summary>
    public string Answer => _answer.Length > 0 ? _answer : _draft;

    public IEnumerable<ChatStreamEvent> Read(JsonNode result)
    {
        var kind = EventFields.Text(result, "kind");
        TaskId ??= kind == "task" ? EventFields.Text(result, "id") : EventFields.Text(result, "taskId");
        return kind switch
        {
            "status-update" => Status(EventFields.Get(result, "status"), EventFields.Flag(result, "final")),
            "task" => Status(EventFields.Get(result, "status"), IsTerminal(EventFields.Text(EventFields.Get(result, "status"), "state"))),
            "artifact-update" => Artifact(EventFields.Get(result, "artifact"), EventFields.Flag(result, "append")),
            "message" => Message(result, final: true),
            _ => []
        };
    }

    // A turn that ends badly says so in fixed words: kagent's own text about it (a stack, a pod name) goes nowhere.
    private IEnumerable<ChatStreamEvent> Status(JsonNode? status, bool final)
    {
        Failure ??= final
            ? EventFields.Text(status, "state") switch
            {
                "failed" or "rejected" or "canceled" => ChatFailure.Unfinished,
                "input-required" or "auth-required" => ChatFailure.NeedsApproval,
                _ => null
            }
            : null;
        return Failure is null ? Message(EventFields.Get(status, "message"), final) : [];
    }

    private IEnumerable<ChatStreamEvent> Message(JsonNode? message, bool final)
    {
        if (message is null || EventFields.Text(message, "role") == "user")
            yield break;
        var partial = IsPartial(EventFields.Get(message, "metadata"));
        if (!partial && UsageOf(EventFields.Get(message, "metadata")) is { Total: > 0 } usage)
        {
            Usage = Usage.Add(usage);
            yield return ChatStreamEvent.Usage(Usage);
        }
        foreach (var e in Parts(EventFields.Get(message, "parts"), partial, final))
            yield return e;
    }

    private IEnumerable<ChatStreamEvent> Artifact(JsonNode? artifact, bool append)
    {
        var partial = IsPartial(EventFields.Get(artifact, "metadata"));
        foreach (var e in Parts(EventFields.Get(artifact, "parts"), partial, final: !partial, append))
            yield return e;
    }

    private IEnumerable<ChatStreamEvent> Parts(JsonNode? parts, bool partial, bool final, bool append = false)
    {
        var text = new StringBuilder();
        foreach (var part in parts as JsonArray ?? [])
        {
            var type = EventFields.Text(EventFields.Get(part, "metadata"), "adkType") ?? EventFields.Text(EventFields.Get(part, "metadata"), "kagentType");
            var data = EventFields.Get(part, "data");
            if (type == "function_call")
            {
                if (Step(text, partial) is { } step)
                    yield return step;
                yield return ChatStreamEvent.Item(ToolSteps.Call(EventFields.Text(data, "id"), EventFields.Text(data, "name"), EventFields.Get(data, "args"), null));
            }
            else if (type == "function_response")
                yield return ChatStreamEvent.Item(ToolSteps.Result(EventFields.Text(data, "id"), EventFields.Text(data, "name"), EventFields.Get(data, "response"), null));
            else if (EventFields.Text(part, "kind") == "text" && !SessionEvents.IsThought(part))
                text.Append(EventFields.Text(part, "text"));
        }
        if (text.Length == 0)
            yield break;
        if (partial)
        {
            _draft += text;
            yield return ChatStreamEvent.Delta(text.ToString());
        }
        else if (final)
        {
            _answer = append ? _answer + text : text.ToString();
            _draft = "";
            yield return ChatStreamEvent.Answer(_answer);
        }
        else
        {
            _draft = text.ToString();
            yield return ChatStreamEvent.Text(_draft);
        }
    }

    // What the model said before calling a tool is a step, not the answer (D316).
    private ChatStreamEvent? Step(StringBuilder text, bool partial)
    {
        var said = partial ? _draft + text : text.Length > 0 ? text.ToString() : _draft;
        text.Clear();
        _draft = "";
        return said.Trim().Length > 0 ? ChatStreamEvent.Item(new ConversationItem(ConversationItemKinds.Step, said)) : null;
    }

    private static bool IsTerminal(string? state) => state is "completed" or "failed" or "rejected" or "canceled" or "input-required" or "auth-required";

    private static bool IsPartial(JsonNode? metadata) => EventFields.Flag(metadata, "adkPartial") || EventFields.Flag(metadata, "kagentPartial");

    private static ChatUsage UsageOf(JsonNode? metadata) =>
        SessionEvents.Usage(EventFields.Get(metadata, "adkUsageMetadata") ?? EventFields.Get(metadata, "kagentUsageMetadata"));
}
