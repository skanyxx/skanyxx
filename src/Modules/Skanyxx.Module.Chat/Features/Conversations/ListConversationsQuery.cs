using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary><paramref name="UserId"/>'s own conversations, newest first (the sessions sidebar).</summary>
internal sealed record ListConversationsQuery(string UserId) : IRequest<Outcome<IReadOnlyList<ConversationSummary>>>;
