namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>A kept answer and the conversation it came from (the cached turn continues there).</summary>
internal sealed record CachedAnswer(string ConversationId, string Content, DateTime CreatedAt);
