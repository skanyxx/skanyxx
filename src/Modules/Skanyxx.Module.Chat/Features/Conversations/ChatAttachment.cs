namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>A file sent with a user message; its bytes are at <c>api/chat/files/{Id}</c>, for its owner only.</summary>
internal sealed record ChatAttachment(string Id, string Name);
