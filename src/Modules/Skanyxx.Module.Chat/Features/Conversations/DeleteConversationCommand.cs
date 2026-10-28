using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>Deletes the caller's own conversation in kagent, with its files and its chat-index row.</summary>
internal sealed record DeleteConversationCommand(string UserId, string ConversationId) : IRequest<Outcome<bool>>;
