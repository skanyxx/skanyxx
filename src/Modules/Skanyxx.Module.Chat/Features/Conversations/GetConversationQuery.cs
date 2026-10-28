using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary><paramref name="UserId"/>'s own conversation with its messages; anyone else's is "not found".</summary>
internal sealed record GetConversationQuery(string UserId, string ConversationId) : IRequest<Outcome<ConversationView>>;
