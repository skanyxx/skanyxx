using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>
/// One thing shown in a conversation, the same whether it streams in or is read back. <paramref name="Kind"/> is
/// <see cref="ConversationItemKinds"/>. <paramref name="Usage"/> is the turn's tokens, on its answer.
/// </summary>
internal sealed record ConversationItem(
    string Kind,
    string? Text = null,
    ChatUsage? Usage = null,
    DateTimeOffset? At = null,
    IReadOnlyList<ChatAttachment>? Attachments = null);
