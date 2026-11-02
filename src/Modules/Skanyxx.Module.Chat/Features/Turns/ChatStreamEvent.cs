using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Module.Chat.Features.Conversations;

namespace Skanyxx.Module.Chat.Features.Turns;

/// <summary>
/// One server-sent event of a streamed turn (<c>event: {Type}</c>, <c>data: {Data as JSON}</c>):
/// <c>session</c> first; then <c>delta</c> (appended to the draft), <c>text</c> (replaces the draft; empty when a tool
/// call takes it away), <c>usage</c> (the model's tokens so far) and <c>answer</c> (the answer so far, replacing the
/// draft); tool steps are never sent (D555); last <c>done</c> (with <c>clientTokens</c>, Skanyxx's own count of what it sent, D548) or <c>error</c> (with kagent's
/// <c>kagentReason</c> for builders and up, D553).
/// </summary>
internal sealed record ChatStreamEvent(string Type, object Data)
{
    public static ChatStreamEvent Session(string conversationId, bool isNew, string title) => new("session", new { conversationId, isNew, title });

    public static ChatStreamEvent Delta(string text) => new("delta", new { text });

    public static ChatStreamEvent Text(string text) => new("text", new { text });

    public static ChatStreamEvent Usage(ChatUsage usage) => new("usage", usage);

    public static ChatStreamEvent Answer(string text) => new("answer", new { text });

    public static ChatStreamEvent Done(string conversationId, bool cached, ChatUsage usage, long clientTokens, ChatResponse context) =>
        new("done", new { conversationId, cached, usage, clientTokens, relatedSessions = context.RelatedSessions, memoryCards = context.MemoryCards });

    /// <param name="conversationGone">The turn was the conversation's first and it is deleted: a retry starts a new one.</param>
    /// <param name="reason">kagent's own words, cleaned; only for builders and up, else not in the event at all (D553).</param>
    public static ChatStreamEvent Error(string message, bool conversationGone, string? reason = null) =>
        reason is null ? new("error", new { message, conversationGone }) : new("error", new { message, conversationGone, kagentReason = reason });
}
