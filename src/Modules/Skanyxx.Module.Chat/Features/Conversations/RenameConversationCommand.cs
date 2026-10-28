using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Conversations;

internal sealed record RenameConversationCommand(string UserId, string ConversationId, string Title) : IRequest<Outcome<ConversationSummary>>
{
    public const int MaxTitle = 120;
}
