using System.Text;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// Reads one turn's A2A stream from kagent (task, status updates, artifact updates) into <see cref="ChatStreamEvent"/>s.
/// Partial chunks grow the draft; a whole intermediate message replaces it; a tool call clears the draft (what the model
/// said on its way to a tool is not the answer); the final status message or the artifact is the answer. Tool calls and
/// their results never leave the server, and reasoning parts are skipped (D555). A message's usage counts once per message id.
/// </summary>
internal sealed class A2ATurn
{
    private string _draft = "";
    private string _answer = "";
    private readonly HashSet<string> _counted = [];

    public string? TaskId { get; private set; }

    public ChatUsage Usage { get; private set; } = ChatUsage.None;

    /// <summary>Why the turn did not complete, in plain words; null while it is fine.</summary>
    public string? Failure { get; private set; }

    /// <summary>The state a failed turn ended in (<c>failed</c>, <c>rejected</c>, …); null while it is fine.</summary>
    public string? FailedState { get; private set; }

    /// <summary>
    /// kagent's own text about a failed turn (its final status message), raw: for the log and builders and up only, after
    /// <see cref="Core.Platform.Chat.ChatFailureReason"/> (D553).
    /// </summary>
    public string? FailureDetail { get; private set; }

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

    // A turn that ends badly says so in fixed words; kagent's own text about it (a stack, a pod name, an address) is kept
    // apart, for the log and for builders and up (D553).
    private IEnumerable<ChatStreamEvent> Status(JsonNode? status, bool final)
    {
        if (Failure is null && final)
        {
            var state = EventFields.Text(status, "state");
            Failure = state switch
            {
                "failed" or "rejected" or "canceled" => ChatFailure.Unfinished,
                "input-required" or "auth-required" => ChatFailure.NeedsApproval,
                _ => null
            };
            if (Failure is not null)
                (FailedState, FailureDetail) = (state, Said(EventFields.Get(status, "message")));
        }
        return Failure is null ? Message(EventFields.Get(status, "message"), final) : [];
    }

    // A message's text parts, reasoning skipped.
    private static string Said(JsonNode? message) => string.Concat((EventFields.Get(message, "parts") as JsonArray ?? [])
        .Where(p => EventFields.Text(p, "kind") == "text" && !SessionEvents.IsThought(p))
        .Select(p => EventFields.Text(p, "text")));

    private IEnumerable<ChatStreamEvent> Message(JsonNode? message, bool final)
    {
        if (message is null || EventFields.Text(message, "role") == "user")
            yield break;
        var partial = IsPartial(EventFields.Get(message, "metadata"));
        // A message kagent sends twice (in a status and again in the final task) counts once, by its id (D380).
        if (!partial && UsageOf(EventFields.Get(message, "metadata")) is { Total: > 0 } usage
            && (EventFields.Text(message, "messageId") is not { } id || _counted.Add(id)))
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
            if (type == "function_call")
            {
                if (Clear(text, partial))
                    yield return ChatStreamEvent.Text("");
            }
            else if (type == "function_response")
                continue;
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

    // What the model said before calling a tool is not the answer (D316), and no longer shown at all (D555). True when
    // the page was showing it, so it is told to take it away.
    private bool Clear(StringBuilder text, bool partial)
    {
        var shown = _draft.Length > 0 || (partial && text.Length > 0);
        text.Clear();
        _draft = "";
        return shown;
    }

    private static bool IsTerminal(string? state) => state is "completed" or "failed" or "rejected" or "canceled" or "input-required" or "auth-required";

    private static bool IsPartial(JsonNode? metadata) => EventFields.Flag(metadata, "adkPartial") || EventFields.Flag(metadata, "kagentPartial");

    private static ChatUsage UsageOf(JsonNode? metadata) =>
        SessionEvents.Usage(EventFields.Get(metadata, "adkUsageMetadata") ?? EventFields.Get(metadata, "kagentUsageMetadata"));
}
