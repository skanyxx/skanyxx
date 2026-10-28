using MediatR;

namespace Skanyxx.Core.Platform.Chat;

/// <summary><paramref name="UserId"/> renamed their conversation in Chat; the chat index shows the same title.</summary>
public sealed record ChatConversationRenamed(string UserId, string ConversationId, string Title) : INotification;
