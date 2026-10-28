using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// One thing shown in a conversation, the same whether it streams in or is read back. <paramref name="Kind"/> is
/// <see cref="ConversationItemKinds"/>. Tool <paramref name="Arguments"/> and <paramref name="Result"/> are redacted and
/// cut before they leave the server. <paramref name="Agent"/> is set when the tool is another agent (<c>namespace/name</c>).
/// <paramref name="Usage"/> is the turn's tokens, on its answer.
/// </summary>
internal sealed record ConversationItem(
    string Kind,
    string? Text = null,
    string? CallId = null,
    string? Tool = null,
    string? Agent = null,
    string? Arguments = null,
    string? Result = null,
    bool IsError = false,
    ChatUsage? Usage = null,
    DateTimeOffset? At = null,
    IReadOnlyList<ChatAttachment>? Attachments = null);
