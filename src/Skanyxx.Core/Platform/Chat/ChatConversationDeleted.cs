using MediatR;

namespace Skanyxx.Core.Platform.Chat;

/// <summary><paramref name="UserId"/> deleted their conversation in Chat (kagent's session is gone); its index row and files go too.</summary>
public sealed record ChatConversationDeleted(string UserId, string ConversationId) : INotification;
